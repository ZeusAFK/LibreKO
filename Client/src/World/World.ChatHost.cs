using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const double NearbyTickSeconds = 1.0;
    private const int NearbyPollTicks = 5;
    private const int ChatColorsLayer = 74;
    private const float SwatchWidth = 36f;
    private const float SwatchHeight = 18f;
    private const int SwatchColumns = 6;

    private NearbyCard? _nearbyCard;
    private Godot.Timer? _nearbyTimer;
    private List<Net.NearbyPlayer> _nearbyListed = new();
    private List<NearbyRow> _nearbyRows = new();
    private int _nearbyTicks;
    private bool _nearbyFirstPoll = true;
    private bool _nearbyRebuildQueued;

    private CanvasLayer? _chatColorsLayer;
    private HudWindow? _chatColorsWindow;
    private ChatColors _chatColorsDraft = new();
    private readonly ColorRect[] _chatColorSwatches = new ColorRect[ChatColors.SlotCount];
    private PopupPanel? _chatPalette;
    private int _chatPaletteSlot;

    private void ChatHostInit()
    {
        Chat.NameMenu = ShowPlayerMenuByName;
        Chat.ItemTipShow = ShowChatItemTip;
        Chat.ItemTipHide = HideItemTooltip;
        Chat.IsFriend = IsFriendName;
        Chat.ColorsRequested = OpenChatColors;
        Chat.StatusNotice = CombatNotice;
        Chat.LayoutChanged += OnChatLayoutChanged;
        Net.I.NearbyPlayersEvent += OnNearbyPlayers;
        if (!Platform.TouchUi) BuildNearbyCard();
    }

    private void ChatHostDispose()
    {
        Net.I.NearbyPlayersEvent -= OnNearbyPlayers;
        if (Chat == null) return;
        Chat.LayoutChanged -= OnChatLayoutChanged;
    }

    private void ShowChatItemTip(int itemId) =>
        ShowItemTooltip(-1, new ItemSlot { ItemId = itemId, Count = 1, Durability = ItemData.MaxDurabilityOf(itemId) });

    private bool IsFriendName(string name) =>
        _friends.Exists(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private void OnChatLayoutChanged() => QueueDockLayout();

    private void BuildNearbyCard()
    {
        _nearbyCard = new NearbyCard();
        Chat.DockNearby(_nearbyCard);
        _nearbyCard.WhisperPicked += row => OpenWhisperWith(row.Name);
        _nearbyCard.MenuPicked += (row, at) => ShowPlayerMenuByName(row.Name, at);
        _nearbyTimer = new Godot.Timer { WaitTime = NearbyTickSeconds, OneShot = false, Autostart = true };
        _nearbyTimer.Timeout += NearbyTick;
        AddChild(_nearbyTimer);
    }

    private void NearbyTick()
    {
        if (!_worldReady || _self == null || _nearbyCard?.IsVisibleInTree() != true) return;
        if (_nearbyTicks++ % NearbyPollTicks == 0)
        {
            Net.I.SendNearbyPlayersRequest(_nearbyFirstPoll);
            _nearbyFirstPoll = false;
        }
        RebuildNearby();
    }

    private void OnNearbyPlayers(List<Net.NearbyPlayer> list)
    {
        _nearbyListed = list;
        if (_nearbyRebuildQueued || _nearbyCard?.IsVisibleInTree() != true) return;
        _nearbyRebuildQueued = true;
        Callable.From(() =>
        {
            _nearbyRebuildQueued = false;
            RebuildNearby();
        }).CallDeferred();
    }

    private NearbyViewer NearbyViewerNow() =>
        new(Net.I.LastEnter.Name, Net.I.Nation, Net.I.MyClan.ClanId, _myKoX, _myKoZ, Net.I.IsGm);

    private void RebuildNearby()
    {
        if (_nearbyCard == null) return;
        var me = NearbyViewerNow();
        var seen = new List<NearbySeen>();
        foreach (var (id, e) in _ents)
        {
            if (e.IsNpc || id == _myId || e.Name.Length == 0) continue;
            if ((e.Infiltrating || e.StealthUndetected) && !me.Gm) continue;
            seen.Add(new NearbySeen(id, e.Name, e.Nation, e.Level, 0, e.KnightsId, e.KoX, e.KoZ, e.IsGm));
        }
        var listed = new List<NearbyListed>(_nearbyListed.Count);
        foreach (var p in _nearbyListed) listed.Add(new NearbyListed(p.Name, p.Nation, p.KoX, p.KoZ, p.ClanId));
        var party = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in Net.I.Party) party.Add(member.Name);

        var rows = NearbyRoster.Build(me, listed, seen, party);
        if (NearbyRoster.SameRows(rows, _nearbyRows)) return;
        _nearbyRows = rows;
        _nearbyCard.SetRows(rows);
    }

    private void OpenChatColors()
    {
        if (_chatColorsWindow == null) BuildChatColors();
        _chatColorsDraft = ChatColors.Parse(Chat.Colors.Format());
        RefreshChatSwatches();
        _chatColorsWindow!.Visible = true;
    }

    private void BuildChatColors()
    {
        _chatColorsLayer = new CanvasLayer { Layer = ChatColorsLayer };
        AddChild(_chatColorsLayer);
        _chatColorsWindow = new HudWindow("chat_colors", "Chat Colours") { Visible = false };
        _chatColorsWindow.Closed += () => _chatColorsWindow.Visible = false;
        _chatColorsLayer.AddChild(_chatColorsWindow);

        var root = _chatColorsWindow.Body;
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
            button.Pressed += () => OpenChatPalette(slot, button);
            _chatColorSwatches[i] = swatch;
            row.AddChild(button);
            root.AddChild(row);
        }

        var foot = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        foot.AddThemeConstantOverride("separation", 6);
        var reset = UiTheme.SmallButton("Default", "Restore the default colours");
        reset.Pressed += () =>
        {
            _chatColorsDraft.Reset();
            RefreshChatSwatches();
        };
        foot.AddChild(reset);
        var apply = UiTheme.ActionButton("Apply", "Use these colours");
        apply.Pressed += () => Chat.ApplyColors(ChatColors.Parse(_chatColorsDraft.Format()));
        foot.AddChild(apply);
        root.AddChild(foot);

        _chatPalette = new PopupPanel();
        var grid = new GridContainer { Columns = SwatchColumns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        _chatPalette.AddChild(grid);
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
                _chatColorsDraft[(ChatColorSlot)_chatPaletteSlot] = colour;
                RefreshChatSwatches();
                _chatPalette.Hide();
            };
            grid.AddChild(pick);
        }
        _chatColorsWindow.AddChild(_chatPalette);
    }

    private void OpenChatPalette(int slot, Control anchor)
    {
        if (_chatPalette == null) return;
        _chatPaletteSlot = slot;
        _chatPalette.ResetSize();
        var at = anchor.GetScreenPosition() + new Vector2(0, anchor.Size.Y + 2);
        _chatPalette.Position = (Vector2I)at;
        _chatPalette.Popup();
    }

    private void RefreshChatSwatches()
    {
        for (int i = 0; i < ChatColors.SlotCount; i++)
            if (_chatColorSwatches[i] != null) _chatColorSwatches[i].Color = _chatColorsDraft[(ChatColorSlot)i];
    }
}
