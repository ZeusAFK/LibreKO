using Godot;

namespace LibreKO.Features.Windows;

internal sealed class WarpFeature : IFeature, IClosableWindow
{
    private readonly IWorldContext context;
    private readonly NpcQuestSession npcQuest;
    private readonly ProgressSession progress;
    private readonly SelfSession self;
    private readonly WarpSession warp;
    private readonly IWarpRequests requests;
    private readonly MapInteractablesSystem mapInteractables;
    private readonly IInteractionDialogs interactionDialogs;

    public WarpFeature(IWorldContext context, NpcQuestSession npcQuest, ProgressSession progress, SelfSession self, WarpSession warp, IWarpRequests requests, MapInteractablesSystem mapInteractables, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.npcQuest = npcQuest;
        this.progress = progress;
        this.self = self;
        this.warp = warp;
        this.requests = requests;
        this.mapInteractables = mapInteractables;
        this.interactionDialogs = interactionDialogs;
    }

    private static readonly Color RowLocked = new("b50008");
    private static readonly Color RowOpen = new("53cc7e");
    private static readonly Color RowSelected = new("edbf72");
    private const int ImageWidth = 168;
    private const int ImageHeight = 193;
    private const int ListHeight = 331;

    private (int SourceId, string Title)? openedGate;
    private CanvasLayer layer = null!;
    internal HudWindow Panel { get; private set; } = null!;
    private VBoxContainer list = null!;
    private ScrollContainer scroll = null!;
    private TextureRect image = null!;
    private Label levelsLabel = null!;
    private Label desc = null!;
    private Label goldLbl = null!;
    private Label status = null!;
    private Button travel = null!;
    private bool shown;

    public bool IsOpen => shown;

    internal List<WarpListEntry> Entries { get; } = new();
    private readonly List<PanelContainer> rows = new();
    internal int Selected { get; private set; } = -1;
    private int sourceId;

    public void Build()
    {
        interactionDialogs.InteractionCloses(this);
        BuildPanel();
        warp.WarpListEvent += OnList;
        warp.WarpFailEvent += OnFail;
        progress.GoldChangeEvent += OnGold;
        mapInteractables.WarpGateOpened += OpenedFromGate;
    }

    public void Dispose()
    {
        warp.WarpListEvent -= OnList;
        warp.WarpFailEvent -= OnFail;
        progress.GoldChangeEvent -= OnGold;
        mapInteractables.WarpGateOpened -= OpenedFromGate;
    }

    private void OpenedFromGate(int sourceId, string title) => openedGate = (sourceId, title);

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Panel = new HudWindow("warp", "Warp List") { Visible = false };
        Panel.Closed += Close;
        layer.AddChild(Panel);

        var root = Panel.Body;
        root.AddThemeConstantOverride("separation", 8);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        root.AddChild(top);

        var left = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        left.AddThemeConstantOverride("separation", 5);
        top.AddChild(left);

        var frame = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        left.AddChild(frame);
        image = new TextureRect
        {
            CustomMinimumSize = new Vector2(ImageWidth, ImageHeight),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        frame.AddChild(image);

        var levelChip = new PanelContainer();
        levelChip.AddThemeStyleboxOverride("panel", UiTheme.Chip());
        left.AddChild(levelChip);
        levelsLabel = UiTheme.Text("", 13, UiTheme.GoldBright, HorizontalAlignment.Center);
        levelChip.AddChild(levelsLabel);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(196, ListHeight),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        this.scroll = scroll;
        top.AddChild(scroll);
        list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 2);
        scroll.AddChild(list);

        root.AddChild(UiTheme.SectionTitle("Warning"));
        var descBox = new PanelContainer();
        descBox.AddThemeStyleboxOverride("panel", UiTheme.Inset());
        root.AddChild(descBox);
        var descMargin = new MarginContainer();
        UiTheme.Margins(descMargin, 6);
        descBox.AddChild(descMargin);
        desc = UiTheme.Text("", 12, UiTheme.TextLo);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.CustomMinimumSize = new Vector2(384, 76);
        desc.VerticalAlignment = VerticalAlignment.Top;
        descMargin.AddChild(desc);

        root.AddChild(new HSeparator());
        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 8);
        goldLbl = UiTheme.Text("", 13, UiTheme.Gold);
        goldLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(goldLbl);
        status = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Right);
        status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(status);
        root.AddChild(footer);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        root.AddChild(buttons);
        travel = new Button { Text = "Travel", FocusMode = Control.FocusModeEnum.None };
        travel.CustomMinimumSize = new Vector2(120, 0);
        travel.Pressed += TravelSelected;
        buttons.AddChild(travel);
        var close = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
        close.CustomMinimumSize = new Vector2(120, 0);
        close.Pressed += Close;
        buttons.AddChild(close);
    }

    internal void OnList(List<WarpListEntry> warps)
    {
        Entries.Clear();
        Entries.AddRange(warps);

        if (Entries.Count == 0)
        {
            openedGate = null;
            if (shown) SetStatus("No destinations available.", true);
            return;
        }

        context.NpcDialog.Close();
        sourceId = openedGate?.SourceId ?? npcQuest.LastNpcId;
        Title = openedGate?.Title ?? npcQuest.LastNpcName;
        Panel.Title = Title;
        Selected = -1;
        SetStatus("", false);
        RefreshGold();
        RefreshList();
        SelectRow(WarpRows.FirstUsable(Entries.ConvertAll(e => BlockReason(e, WarpInfo(e)))));

        Panel.Visible = true;
        shown = true;
        context.Plugins.Warp();
    }

    public void Close()
    {
        if (!shown) return;
        shown = false;
        openedGate = null;
        Panel.Visible = false;
        context.Plugins.Warp();
    }

    private void RefreshList()
    {
        foreach (var c in list.GetChildren()) c.QueueFree();
        rows.Clear();
        for (int i = 0; i < Entries.Count; i++)
        {
            var row = BuildRow(i, Entries[i]);
            rows.Add(row);
            list.AddChild(row);
        }
    }

    internal string Title { get; private set; } = "";

    private const int RowThumbSize = 26;
    private const int RowLockSize = 14;
    private const int RowSideFont = 11;
    private const float RowBlockedAlpha = 0.55f;

    internal static WarpData.Destination WarpInfo(WarpListEntry entry) =>
        WarpData.TryGet(entry.WarpId, out var info) ? info : new WarpData.Destination(entry.Name, entry.Announce, "", 0, 0);

    private PanelContainer BuildRow(int index, WarpListEntry entry)
    {
        var info = WarpInfo(entry);
        string? blocked = BlockReason(entry, info);

        var row = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, TooltipText = blocked ?? "" };
        row.AddThemeStyleboxOverride("panel", UiTheme.Row());

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 7, 3, 7, 3);
        margin.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(margin);

        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 8);
        line.Modulate = new Color(1f, 1f, 1f, blocked != null ? RowBlockedAlpha : 1f);
        margin.AddChild(line);

        line.AddChild(new TextureRect
        {
            Texture = WarpImages.Get(info.Image) ?? UiIcons.Get("system/door"),
            CustomMinimumSize = new Vector2(RowThumbSize, RowThumbSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        if (blocked != null)
            line.AddChild(new TextureRect
            {
                Texture = UiIcons.Get("system/lock"),
                CustomMinimumSize = new Vector2(RowLockSize, RowLockSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                Modulate = UiTheme.Bad,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

        var label = UiTheme.Text(info.Name, 13, RowColor(info, false));
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Name = "name";
        line.AddChild(label);

        var side = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        side.AddThemeConstantOverride("separation", 0);
        line.AddChild(side);
        string levels = WarpRows.LevelText(info.MinLevel, info.MaxLevel);
        if (levels.Length > 0) side.AddChild(UiTheme.Text(levels, RowSideFont, UiTheme.TextDim, HorizontalAlignment.Right));
        side.AddChild(UiTheme.Text(WarpRows.FeeText(entry.Fee), RowSideFont,
            entry.Fee > 0 ? UiTheme.Gold : UiTheme.Good, HorizontalAlignment.Right));

        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            {
                SelectRow(index);
                if (click.DoubleClick) TravelSelected();
            }
        };
        return row;
    }

    private Color RowColor(WarpData.Destination info, bool isSelected) =>
        isSelected ? RowSelected
        : self.Sheet.Level < info.MinLevel ? RowLocked
        : RowOpen;

    internal void SelectRow(int index)
    {
        if (index < 0 || index >= Entries.Count) return;
        Selected = index;

        for (int i = 0; i < rows.Count; i++)
        {
            var rowInfo = WarpInfo(Entries[i]);
            bool isSelected = i == index;
            rows[i].AddThemeStyleboxOverride("panel", UiTheme.Row(isSelected));
            if (rows[i].FindChild("name", true, false) is Label label)
                label.AddThemeColorOverride("font_color", RowColor(rowInfo, isSelected));
        }

        var selectedRow = rows[index];
        Callable.From(() => scroll.EnsureControlVisible(selectedRow)).CallDeferred();

        var entry = Entries[index];
        var info = WarpInfo(entry);
        image.Texture = WarpImages.Get(info.Image);
        levelsLabel.Text = WarpRows.LevelText(info.MinLevel, info.MaxLevel);
        desc.Text = info.Description;
        string? blocked = BlockReason(entry, info);
        SetStatus(blocked ?? $"Fee  {WarpRows.FeeText(entry.Fee)}", blocked != null);
        travel.Disabled = blocked != null;
        context.Plugins.Warp();
    }

    internal string? BlockReason(WarpListEntry entry, WarpData.Destination info) =>
        WarpRows.BlockReason(self.Sheet.Level, self.Sheet.Gold, info.MinLevel, info.MaxLevel, entry.Fee);

    internal bool TryEnter()
    {
        if (!shown || travel.Disabled) return false;
        TravelSelected();
        return true;
    }

    internal void TravelSelected()
    {
        if (!shown || context.Self.Dead) return;
        if (Selected < 0 || Selected >= Entries.Count) return;

        var entry = Entries[Selected];
        var info = WarpInfo(entry);
        if (BlockReason(entry, info) is { } reason)
        {
            SetStatus(reason, true);
            return;
        }

        requests.Select(sourceId, entry.WarpId);
        context.Log.Notice(entry.Fee > 0
            ? $"Travelling to {info.Name} (−{entry.Fee:n0} gold)…"
            : $"Travelling to {info.Name}…");
        Close();
    }

    private void OnFail(WarpResult result, int detail)
    {
        openedGate = null;
        if (RefusalText(result, detail) is not { } reason) return;
        if (shown) SetStatus(reason, true);
        else context.Log.Notice(reason);
    }

    private string? RefusalText(WarpResult result, int detail) => result switch
    {
        WarpResult.LevelTooLow => ItemData.Text(6610, "You need to be at least level %d.").Replace("%d", detail.ToString()),
        WarpResult.CastleSiege => ItemData.Text(6612, "You cannot enter during the Castle Siege War."),
        WarpResult.LunarWar => ItemData.Text(6611, "You cannot enter during the Lunar War."),
        WarpResult.QuestCompleted => ItemData.Text(7668, "You can't enter beause you completed %s quest.")
            .Replace("%s", QuestData.Name(detail, self.LastEnter.Name ?? "")),
        WarpResult.NoNationalPoints => ItemData.Text(6613, "You cannot enter when you have 0 national points."),
        WarpResult.LevelRangeOnly => ItemData.Text(7657, "Only characters with level 30~50 can enter."),
        WarpResult.NotQualified => ItemData.Text(7659, "You cannot enter because you do not qualify."),
        WarpResult.TradeCooldown => ItemData.Text(6614, "You can't teleport for 2 minutes after trading"),
        WarpResult.ServerFull => ItemData.Text(7666, "Arena Server is full to capacity. Please try again later."),
        WarpResult.ClanGrade => ItemData.Text(7669, "You cannot enter because your clan grade is too low"),
        WarpResult.SameAddress => ItemData.Text(6621, "Attending event with multiple characters from the same IP, is not allowed."),
        WarpResult.Captcha => ItemData.Text(6622, "Failed to enter (Reason: Captcha penalty)"),
        _ => null,
    };

    private void OnGold(GoldChange change)
    {
        if (!shown) return;
        RefreshGold();
        SelectRow(Selected);
    }

    private void RefreshGold() => goldLbl.Text = $"Gold  {self.Sheet.Gold:n0}";

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }
}
