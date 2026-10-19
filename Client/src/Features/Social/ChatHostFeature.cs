using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Social;

internal sealed class ChatHostFeature : IFeature, IEscapable, IChatHost
{
    private readonly IWorldContext context;
    private readonly WorldRoot world;
    private readonly ClanSession clan;
    private readonly NearbyPlayersSession nearbyPlayers;
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly FriendsSession friends;
    private readonly INearbyPlayersRequests nearbyPlayersRequests;

    public ChatHostFeature(IWorldContext context, WorldRoot world, ClanSession clan, NearbyPlayersSession nearbyPlayers, PartySession party, SelfSession self, FriendsSession friends, INearbyPlayersRequests nearbyPlayersRequests)
    {
        this.context = context;
        this.world = world;
        this.clan = clan;
        this.nearbyPlayers = nearbyPlayers;
        this.party = party;
        this.self = self;
        this.friends = friends;
        this.nearbyPlayersRequests = nearbyPlayersRequests;
    }

    private const double NearbyTickSeconds = 1.0;
    private const int NearbyPollTicks = 5;
    private const float SwatchWidth = 36f;
    private const float SwatchHeight = 18f;
    private const int SwatchColumns = 6;

    internal NearbyCard? NearbyCard { get; set; }
    private Godot.Timer? nearbyTimer;

    internal List<NearbyRow> NearbyRows { get; set; } = new();
    private int nearbyTicks;
    private bool nearbyFirstPoll = true;
    private bool nearbyRebuildQueued;
    private CanvasLayer? colorsLayer;
    internal HudWindow? ColorsWindow { get; private set; }
    private ChatColors colorsDraft = new();
    internal ColorRect[] ColorSwatches { get; } = new ColorRect[ChatColors.SlotCount];
    private PopupPanel? palette;
    private int paletteSlot;

    public void Build()
    {
        context.Chat.Attach(this);
        context.Chat.NameMenuRequested += ShowNameMenu;
        context.Chat.ItemTipRequested += ShowItemTip;
        context.Chat.ItemTipDismissed += HideItemTip;
        context.Chat.ColorsRequested += OpenColors;
        context.Chat.StatusNotice += OnStatusNotice;
        context.Chat.NationChatRequested += ToggleNationChat;
        context.Chat.GestureRequested += SendGesture;
        context.Chat.LayoutChanged += OnLayoutChanged;
        nearbyPlayers.NearbyPlayersEvent += OnNearbyPlayers;
        if (!Platform.TouchUi) BuildNearbyCard();
    }

    bool IEscapable.CloseOnEscape()
    {
        if (ColorsWindow is { Visible: true }) CloseColors();
        else return false;
        return true;
    }

    public void Dispose()
    {
        nearbyPlayers.NearbyPlayersEvent -= OnNearbyPlayers;
        if (context.Chat == null) return;
        context.Chat.NameMenuRequested -= ShowNameMenu;
        context.Chat.ItemTipRequested -= ShowItemTip;
        context.Chat.ItemTipDismissed -= HideItemTip;
        context.Chat.ColorsRequested -= OpenColors;
        context.Chat.StatusNotice -= OnStatusNotice;
        context.Chat.NationChatRequested -= ToggleNationChat;
        context.Chat.GestureRequested -= SendGesture;
        context.Chat.LayoutChanged -= OnLayoutChanged;
        context.Chat.Detach(this);
    }

    public bool RunLocalCommand(string command) => world.CommandList.RunLocalCommand(command);

    public bool IsFriend(string name) => IsFriendName(name);

    public bool NoahKnight => world.EntityPlatesSystem.SelfIsNoahKnight;

    public string? SendRefusal() => world.KnightRoyaleWindow.ChatRefusal();

    private void ShowNameMenu(string name, Vector2 at) => world.PlayerMenu.ShowByName(name, at);

    private void ShowItemTip(int itemId) => context.Tooltips.ShowItem(itemId);

    private void HideItemTip() => world.ItemTooltip.Hide();

    private void OnStatusNotice(string text) => context.Log.Notice(text);

    private void ToggleNationChat() => world.NationChannels.ToggleNation();

    private void SendGesture(int gesture) => world.EntityStatesSystem.SendChatGesture(gesture);

    public int PersonalRankOf(int charId) =>
        charId == context.Self.CharId ? self.PersonalRank
        : context.Entities.Views.TryGetValue(charId, out var e) ? e.PersonalRank : NationRankAura.Unranked;

    internal bool IsFriendName(string name) => friends.IsFriend(name);

    private void OnLayoutChanged() => world.MainWindows.QueueDockLayout();

    private void BuildNearbyCard()
    {
        NearbyCard = new NearbyCard();
        context.Chat.DockNearby(NearbyCard);
        NearbyCard.WhisperPicked += row =>
        {
            if (!ZoneLooks.KeepsNamesOutOfChat(context.Zone)) world.Whispers.OpenWhisperWith(row.Name);
        };
        NearbyCard.MenuPicked += (row, at) => world.PlayerMenu.ShowByName(row.Name, at);
        nearbyTimer = new Godot.Timer { WaitTime = NearbyTickSeconds, OneShot = false, Autostart = true };
        nearbyTimer.Timeout += NearbyTick;
        context.Root.AddChild(nearbyTimer);
    }

    private void NearbyTick()
    {
        if (!context.Ready || context.Self.Body == null || NearbyCard?.IsVisibleInTree() != true) return;
        if (nearbyTicks++ % NearbyPollTicks == 0)
        {
            nearbyPlayersRequests.Request(nearbyFirstPoll);
            nearbyFirstPoll = false;
        }
        RebuildNearby();
    }

    private void OnNearbyPlayers(List<NearbyPlayer> list)
    {
        if (nearbyRebuildQueued || NearbyCard?.IsVisibleInTree() != true) return;
        nearbyRebuildQueued = true;
        Callable.From(() =>
        {
            nearbyRebuildQueued = false;
            RebuildNearby();
        }).CallDeferred();
    }

    private NearbyViewer NearbyViewerNow() =>
        new(self.LastEnter.Name, self.Nation, clan.MyClan.ClanId, context.Self.MapXZ.X, context.Self.MapXZ.Z, self.IsGm);

    private void RebuildNearby()
    {
        if (NearbyCard == null) return;
        var me = NearbyViewerNow();
        var seen = new List<NearbySeen>();
        foreach (var (id, e) in context.Entities.Views)
        {
            if (e.IsNpc || id == context.Self.CharId || e.Name.Length == 0) continue;
            if ((e.Infiltrating || e.StealthUndetected) && !me.Gm) continue;
            seen.Add(new NearbySeen(id, e.Name, e.Nation, e.Level, 0, e.KnightsId, e.MapX, e.MapZ, e.IsGm));
        }
        var listed = new List<NearbyListed>(nearbyPlayers.Players.Count);
        foreach (var p in nearbyPlayers.Players) listed.Add(new NearbyListed(p.Name, p.Nation, p.MapX, p.MapZ, p.ClanId));
        var partyMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in party.Members) partyMembers.Add(member.Name);

        var rows = NearbyRoster.Build(me, listed, seen, partyMembers);
        if (NearbyRoster.SameRows(rows, NearbyRows)) return;
        NearbyRows = rows;
        NearbyCard.SetRows(rows);
    }

    internal void OpenColors()
    {
        if (ColorsWindow == null) BuildColors();
        colorsDraft = ChatColors.Parse(context.Chat.Colors.Format());
        RefreshSwatches();
        ColorsWindow!.Visible = true;
    }

    internal void CloseColors()
    {
        palette?.Hide();
        ColorsWindow!.Visible = false;
    }

    private void BuildColors()
    {
        colorsLayer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(colorsLayer);
        ColorsWindow = new HudWindow("chat_colors", "Chat Colours") { Visible = false };
        ColorsWindow.Closed += CloseColors;
        colorsLayer.AddChild(ColorsWindow);

        var root = ColorsWindow.Body;
        root.AddThemeConstantOverride("separation", 4);
        for (int i = 0; i < ChatColors.SlotCount; i++)
        {
            int slot = i;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            var label = UiTheme.Text(ChatColors.SlotLabels[i], 12, UiTheme.TextHi);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(label);
            var button = UiTheme.SmallButton("", "Pick a colour");
            button.CustomMinimumSize = new Vector2(SwatchWidth + 12, 0);
            var swatch = new ColorRect
            {
                CustomMinimumSize = new Vector2(SwatchWidth, SwatchHeight),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
                OffsetLeft = -SwatchWidth / 2, OffsetRight = SwatchWidth / 2,
                OffsetTop = -SwatchHeight / 2, OffsetBottom = SwatchHeight / 2,
            };
            button.AddChild(swatch);
            button.Pressed += () => OpenPalette(slot, button);
            ColorSwatches[i] = swatch;
            row.AddChild(button);
            root.AddChild(row);
        }

        var foot = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        foot.AddThemeConstantOverride("separation", 6);
        var reset = UiTheme.SmallButton("Default", "Restore the default colours");
        reset.Pressed += () =>
        {
            colorsDraft.Reset();
            RefreshSwatches();
        };
        foot.AddChild(reset);
        var apply = UiTheme.ActionButton("Apply", "Use these colours");
        apply.Pressed += () => context.Chat.ApplyColors(ChatColors.Parse(colorsDraft.Format()));
        foot.AddChild(apply);
        root.AddChild(foot);

        palette = new PopupPanel();
        var grid = new GridContainer { Columns = SwatchColumns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        palette.AddChild(grid);
        foreach (var colour in ChatColors.Palette)
        {
            var pick = new Button { CustomMinimumSize = new Vector2(SwatchHeight + 6, SwatchHeight + 6), FocusMode = Control.FocusModeEnum.None };
            var fill = new StyleBoxFlat { BgColor = colour, BorderColor = new Color(0, 0, 0, 0.6f) };
            fill.SetBorderWidthAll(1);
            fill.SetCornerRadiusAll(3);
            pick.AddThemeStyleboxOverride("normal", fill);
            var hot = (StyleBoxFlat)fill.Duplicate();
            hot.BorderColor = UiTheme.GoldBright;
            pick.AddThemeStyleboxOverride("hover", hot);
            pick.AddThemeStyleboxOverride("pressed", hot);
            pick.Pressed += () =>
            {
                colorsDraft[(ChatColorSlot)paletteSlot] = colour;
                RefreshSwatches();
                palette.Hide();
            };
            grid.AddChild(pick);
        }
        ColorsWindow.AddChild(palette);
    }

    internal void OpenPalette(int slot, Control anchor)
    {
        if (palette == null) return;
        paletteSlot = slot;
        palette.ResetSize();
        var at = anchor.GetScreenPosition() + new Vector2(0, anchor.Size.Y + 2);
        palette.Position = (Vector2I)at;
        palette.Popup();
    }

    private void RefreshSwatches()
    {
        for (int i = 0; i < ChatColors.SlotCount; i++)
            if (ColorSwatches[i] != null) ColorSwatches[i].Color = colorsDraft[(ChatColorSlot)i];
    }
}
