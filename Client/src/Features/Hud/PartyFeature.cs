using Godot;

namespace LibreKO.Features.Hud;

internal sealed partial class PartyFeature : IFeature, ITickFeature, IPartyPick
{
    private readonly IWorldContext context;
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly IPartyRequests requests;
    private readonly UserVoteFeature userVoteWindow;
    private readonly EntitiesSession entitiesSession;
    private readonly PlayerMenuFeature playerMenu;
    private readonly SkillCastSystem skillCast;

    public PartyFeature(IWorldContext context, PartySession party, SelfSession self, IPartyRequests requests, UserVoteFeature userVoteWindow, EntitiesSession entitiesSession, PlayerMenuFeature playerMenu, SkillCastSystem skillCast)
    {
        this.context = context;
        this.party = party;
        this.self = self;
        this.requests = requests;
        this.userVoteWindow = userVoteWindow;
        this.entitiesSession = entitiesSession;
        this.playerMenu = playerMenu;
        this.skillCast = skillCast;
    }

    private PartyCommanderFeature? commander;
    private PartyWarBoardFeature? warBoard;

    internal void Attach(PartyCommanderFeature commander) => this.commander = commander;

    internal void Detach(PartyCommanderFeature commander)
    {
        if (this.commander == commander) this.commander = null;
    }

    internal void Attach(PartyWarBoardFeature warBoard) => this.warBoard = warBoard;

    internal void Detach(PartyWarBoardFeature warBoard)
    {
        if (this.warBoard == warBoard) this.warBoard = null;
    }

    internal int Pick { get; set; } = -1;

    int IPartyPick.Pick => Pick;
    private int pickSelection = -1;
    private int hudHighlight = -1;

    internal int Highlight => PartyHud.Highlight(context.Selection.SelectedId, party.IsMember(context.Selection.SelectedId), Pick);


    private readonly PartyOrder order = new();

    internal List<PartyMember> Rows => order.Arrange(party.Members, member => member.CharId);

    internal void MoveRow(int memberId, int step)
    {
        if (order.Move(memberId, step)) RefreshHud();
    }

    private bool AmLeader => party.IsLeader(context.Self.CharId);

    public void Tick(double delta, double nowSec)
    {
        HudTick(nowSec);
    }

    public void Build()
    {
        skillCast.AttachPartyPick(this);
        entitiesSession.EntitySpawnEvent += OnEntitySpawn;
        BuildHud();

        party.PartyMemberEvent += OnMember;
        party.PartyRemovedEvent += OnRemoved;
        party.PartyDisbandEvent += OnDisband;
        party.PartyMemberStatsEvent += OnMemberStats;
        party.PartyMemberLevelEvent += OnMemberLevel;
        party.PartyMemberClassEvent += OnMemberClass;
        party.PartyMemberStatusEvent += OnMemberStatus;
        party.PartyMemberBuffsEvent += OnMemberBuffs;
        party.PartySignalEvent += RefreshSignals;
        party.PartyLevelProgressEvent += OnLevelProgress;
        self.SelfHpEvent += OnSelfHp;
        self.SelfMpEvent += OnSelfMp;
    }

    public void Dispose()
    {
        skillCast.DetachPartyPick();
        entitiesSession.EntitySpawnEvent -= OnEntitySpawn;
        party.PartyMemberEvent -= OnMember;
        party.PartyRemovedEvent -= OnRemoved;
        party.PartyDisbandEvent -= OnDisband;
        party.PartyMemberStatsEvent -= OnMemberStats;
        party.PartyMemberLevelEvent -= OnMemberLevel;
        party.PartyMemberClassEvent -= OnMemberClass;
        party.PartyMemberStatusEvent -= OnMemberStatus;
        party.PartyMemberBuffsEvent -= OnMemberBuffs;
        party.PartySignalEvent -= RefreshSignals;
        party.PartyLevelProgressEvent -= OnLevelProgress;
        self.SelfHpEvent -= OnSelfHp;
        self.SelfMpEvent -= OnSelfMp;
    }

    private void OnEntitySpawn(EntitySnapshot info)
    {
        if (info.Id != context.Self.CharId && !info.IsNpc && info.Name.Length > 0) TryRejoin(info.Name);
    }

    internal void RefreshUI() => RefreshHud();

    private const int QuitText = 3414;
    private const int DisbandedText = 3404;

    private void OnMember(PartyMember m)
    {
        RefreshUI();
        context.Entities.RefreshPartyPlates();
    }

    internal void TryRejoin(string name)
    {
        bool member = false;
        foreach (var m in party.Members)
            if (string.Equals(m.Name, name, System.StringComparison.OrdinalIgnoreCase)) member = true;
        if (party.Rejoin.Recall(name, AmLeader, party.Members.Count, PartyWire.MaxMembers, member))
            playerMenu.InviteToParty(name);
    }

    internal string RejoinTip => party.Rejoin.On
        ? ItemData.Text(PartyRejoin.TipOnText, PartyRejoin.TipOnFallback)
        : ItemData.Text(PartyRejoin.TipOffText, PartyRejoin.TipOffFallback);

    internal void ToggleRejoin()
    {
        party.Rejoin.Toggle();
        context.Log.Notice(party.Rejoin.On
            ? ItemData.Text(PartyRejoin.TurnedOnText, PartyRejoin.TurnedOnFallback)
            : ItemData.Text(PartyRejoin.TurnedOffText, PartyRejoin.TurnedOffFallback));
        RefreshConfig();
        context.Plugins.Party();
    }

    private void OnRemoved(int memberId, string who)
    {
        if (memberId == context.Self.CharId) context.Log.Notice(ItemData.Text(QuitText, "You've quit the party."));
        else if (who.Length > 0) context.Log.Notice($"{who} left the party.");
        RefreshUI();
        context.Entities.RefreshPartyPlates();
    }

    private void OnDisband()
    {
        context.Log.Notice(ItemData.Text(DisbandedText, "The party has been disbanded."));
        RefreshUI();
        context.Entities.RefreshPartyPlates();
    }

    private void OnSelfHp(SelfHpChange change) => RefreshRows();

    private void OnSelfMp(VitalChange change) => RefreshRows();

    private void OnMemberStats(PartyMemberVitals vitals) => RefreshRows();

    private void OnMemberLevel(int cid, int level) => RefreshRows();

    private void OnMemberClass(int cid, int cls) => RefreshRows();

    private void OnMemberStatus(int cid, byte statusType, bool applied) => RefreshRows();

    private void OnMemberBuffs(int cid, IReadOnlyList<int> skillIds) => RefreshRows();

    private void OnLevelProgress(int cid, int permille) => RefreshRows();

    private void RefreshRows() => RefreshHud();

    private static string StatusName(byte type) => type switch
    {
        1 => "dot", 2 => "poison", 3 => "disease", 4 => "blind", (byte)PartyStatusIcon.GreyHealth => "grey hp", _ => "",
    };

    private const string HudLayoutId = "hud_party";
    private const int HudHeaderGap = 6;
    private const float RowInsetX = 4f;
    private const float RowInsetY = 3f;
    private const float RowGap = 4f;
    private const float RowBodyWidth = UiKit.PartyHudWidth - 2f * RowInsetX;
    internal const float DiamondSize = 48f;
    private static readonly Rect2 NameLine = new(50f, -5f, 214f, 20f);
    private static readonly Rect2 HpRect = new(43f, 15f, 222f, 9f);
    private static readonly Rect2 MpRect = new(44f, 26f, 221f, 8f);
    private static readonly Rect2 SignalRect = new(266f, 0f, 16f, 14f);
    private const float BuffLeft = 50f;
    private const float BuffTop = 38f;
    private const float BuffIconSize = 16f;
    private const float BuffIconGap = 3f;
    private const int BuffLetterFont = 9;
    private const int HudTitleFont = 16;
    private const int HudNameFont = 14;
    private const int NameLineSeparation = 5;
    private const int TextOutline = 3;
    private const float HeaderIconSize = 20f;
    private const float GearSize = 22f;
    private const int GearIconSize = 14;
    private const int HeaderSeparation = 8;
    private const int HeaderMarginX = 6;
    private const int HeaderMarginY = 3;
    private const float DragThreshold = 6f;
    private const double RowSlideSeconds = 0.12;
    private const int LiftedZ = 1;
    private const float ConfigWidth = 260f;
    private const float ConfigGap = 4f;
    private const int ConfigFont = 12;
    private const int ConfigSeparation = 6;
    private const string RejoinLabel = "Re-invite after disconnection";
    private const string WarBoardLabel = "Show Party War Board";
    private const string CommanderLabel = "Show Commander Board";
    private const string CommanderOnlyTip = "Only the party commander has the commander board.";
    private const string LeaveLabel = "Leave Party";
    private const int TextMakeLeader = 3431;
    private const int TextKick = 3402;
    private const int TextDisband = 3401;
    private const int TextQuit = 3403;
    internal const int MenuMakeLeader = 1;
    internal const int MenuKick = 2;
    internal const int MenuCommander = 3;
    internal static readonly Color BarTrack = new(0.02f, 0.02f, 0.02f, 0.9f);
    internal static readonly Color LeaderNameColour = new(1f, 1f, 0f);
    internal static readonly Color NameColour = new(1f, 1f, 1f);
    private static readonly Color LevelColour = new("e0bf7c");
    private static readonly Color TitleColour = new("e3c88f");
    private static readonly Color GearBg = new(0.08f, 0.09f, 0.11f, 0.95f);
    private static readonly Color GearHoverBg = new(0.13f, 0.14f, 0.17f, 0.98f);
    private static readonly Color TextOutlineColour = new(0f, 0f, 0f, 0.7f);
    private static readonly Color HpRibbonFill = new("f02a2a");
    private static readonly Color MpRibbonFill = new("2f86f0");

    internal static readonly Dictionary<PartyHpLook, Color> HpFills = new()
    {
        [PartyHpLook.Normal] = Color.Color8(168, 22, 22),
        [PartyHpLook.Lasting] = Color.Color8(184, 152, 18),
        [PartyHpLook.Drop] = Color.Color8(161, 81, 107),
        [PartyHpLook.Slow] = Color.Color8(98, 136, 27),
        [PartyHpLook.Undead] = Color.Color8(105, 105, 105),
    };

    internal sealed class HudRow
    {
        public PanelContainer Frame = null!;
        public StyleBoxEmpty Style = null!;
        public Control Body = null!;
        public LevelOrb Diamond = null!;
        public StatBar Hp = null!;
        public StatBar Mp = null!;
        public Label Level = null!;
        public Label Name = null!;
        public SignalIcon Signal = null!;
        public Control Buffs = null!;
        public readonly List<int> BuffIds = new();
        public int MemberId = -1;
        public int Class = -1;
        public float TargetY = float.NaN;
        public bool Fresh;
        public Tween? Slide;
    }

    internal sealed class RowDrag(HudRow row, float pressY, float grab)
    {
        public readonly HudRow Row = row;
        public readonly float PressY = pressY;
        public readonly float Grab = grab;
        public bool Lifted;
        public int Index;
    }

    internal VBoxContainer? Hud { get; private set; }
    private CanvasLayer hudLayer = null!;
    private Control hudList = null!;
    private Button gearButton = null!;
    private readonly HudRow[] hudRows = new HudRow[PartyHud.MaxRows];
    private readonly List<int> hudOrder = new();
    internal RowDrag? Drag { get; set; }
    private PopupMenu hudMenu = null!;
    private int hudMenuMember = -1;
    private long hudSecond = -1;
    private long signalSecond = -1;
    private bool hudCycles;
    internal PopupPanel? ConfigPopup { get; private set; }
    private CheckButton configRejoin = null!;
    private CheckButton configBoard = null!;
    private CheckButton configCommander = null!;

    internal void BuildHud()
    {
        var layer = new CanvasLayer { Layer = UiLayers.Hud };
        context.Root.AddChild(layer);
        hudLayer = layer;
        context.Hud.PluginSeam(layer, LibreKO.Plugins.HudPart.Party);
        var hud = new VBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(UiKit.PartyHudWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        hud.AddThemeConstantOverride("separation", HudHeaderGap);
        layer.AddChild(hud);

        var header = BuildHudHeader();
        hud.AddChild(header);
        hudList = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(UiKit.PartyHudWidth, 0),
        };
        hud.AddChild(hudList);
        for (int i = 0; i < PartyHud.MaxRows; i++)
        {
            hudRows[i] = BuildHudRow();
            hudList.AddChild(hudRows[i].Frame);
        }

        hudMenu = new PopupMenu();
        hudMenu.IdPressed += OnHudMenu;
        layer.AddChild(hudMenu);

        Hud = hud;
        HudLayout.Attach(hud, HudLayoutId, header, () => new Vector2(HudAnchor.Edge, UiKit.PartyHudTop));
        RefreshHud();
    }

    private PanelContainer BuildHudHeader()
    {
        var style = new StyleBoxEmpty();
        style.ContentMarginLeft = style.ContentMarginRight = HeaderMarginX;
        style.ContentMarginTop = style.ContentMarginBottom = HeaderMarginY;
        var header = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        header.AddThemeStyleboxOverride("panel", style);
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", HeaderSeparation);
        header.AddChild(line);
        line.AddChild(new TextureRect
        {
            Texture = UiIcons.Get("system/users-three"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(HeaderIconSize, HeaderIconSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            SelfModulate = TitleColour,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        var title = UiTheme.Text("Party", HudTitleFont, TitleColour);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        line.AddChild(title);
        gearButton = UiTheme.IconButton(UiIcons.Get("system/gear"), "Party settings");
        gearButton.CustomMinimumSize = new Vector2(GearSize, GearSize);
        gearButton.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        gearButton.AddThemeConstantOverride("icon_max_width", GearIconSize);
        gearButton.AddThemeColorOverride("icon_normal_color", UiTheme.TextHi);
        gearButton.AddThemeStyleboxOverride("normal", GearStyle(GearBg, GearBg));
        gearButton.AddThemeStyleboxOverride("pressed", GearStyle(GearBg, GearBg));
        gearButton.AddThemeStyleboxOverride("hover", GearStyle(GearHoverBg, UiTheme.Gold));
        gearButton.Pressed += OpenConfig;
        line.AddChild(gearButton);
        return header;
    }

    private static StyleBoxFlat GearStyle(Color background, Color edge)
    {
        var style = new StyleBoxFlat { BgColor = background, BorderColor = edge };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(2);
        return style;
    }

    private HudRow BuildHudRow()
    {
        var row = new HudRow
        {
            Style = new StyleBoxEmpty(),
            Frame = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop },
            Body = new Control
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(RowBodyWidth, RowHeight - 2f * RowInsetY),
            },
            Diamond = new LevelOrb(DiamondSize)
            {
                Size = new Vector2(DiamondSize, DiamondSize),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            },
            Hp = new StatBar(HpRibbonFill, HpRect.Size, StatBar.RibbonKind.Upper)
            {
                Position = HpRect.Position,
                Size = HpRect.Size,
                Glide = true,
                ShowNumbers = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            },
            Mp = new StatBar(MpRibbonFill, MpRect.Size, StatBar.RibbonKind.Lower)
            {
                Position = MpRect.Position,
                Size = MpRect.Size,
                Glide = true,
                ShowNumbers = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            },
            Level = RowText(LevelColour),
            Name = RowText(NameColour),
            Signal = new SignalIcon
            {
                Position = SignalRect.Position,
                Size = SignalRect.Size,
                CustomMinimumSize = SignalRect.Size,
                MouseFilter = Control.MouseFilterEnum.Pass,
            },
            Buffs = new Control
            {
                Position = new Vector2(BuffLeft, BuffTop),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            },
        };
        row.Style.ContentMarginLeft = row.Style.ContentMarginRight = RowInsetX;
        row.Style.ContentMarginTop = row.Style.ContentMarginBottom = RowInsetY;
        row.Frame.AddThemeStyleboxOverride("panel", row.Style);
        row.Frame.AddChild(row.Body);

        row.Body.AddChild(row.Hp);
        row.Body.AddChild(row.Mp);
        row.Body.AddChild(row.Diamond);
        var line = new HBoxContainer
        {
            Position = NameLine.Position,
            Size = NameLine.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        line.AddThemeConstantOverride("separation", NameLineSeparation);
        line.AddChild(row.Level);
        row.Name.ClipText = true;
        row.Name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        row.Name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        line.AddChild(row.Name);
        row.Body.AddChild(line);
        row.Body.AddChild(row.Signal);
        row.Body.AddChild(row.Buffs);

        row.Frame.GuiInput += ev => OnHudRowInput(row, ev);
        return row;
    }

    private static Label RowText(Color colour)
    {
        var label = UiTheme.Text("", HudNameFont, colour);
        label.AddThemeConstantOverride("outline_size", TextOutline);
        label.AddThemeColorOverride("font_outline_color", TextOutlineColour);
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        return label;
    }

    internal void RefreshHud()
    {
        if (Hud == null) return;
        context.Plugins.Party();
        bool shown = party.InParty;
        if (Hud.Visible != shown) Hud.Visible = shown;
        long second = (long)context.Now;
        hudSecond = second;
        hudCycles = false;
        int leader = party.Members.Count > 0 ? party.Members[0].CharId : -1;
        var members = shown ? Rows : new List<PartyMember>();
        if (members.Count > PartyHud.MaxRows) members.RemoveRange(PartyHud.MaxRows, members.Count - PartyHud.MaxRows);
        hudOrder.Clear();
        foreach (var m in members) hudOrder.Add(m.CharId);
        foreach (var row in hudRows)
            if (row.MemberId >= 0 && !hudOrder.Contains(row.MemberId)) ReleaseRow(row);
        foreach (var m in members)
            FillHudRow(RowFor(m.CharId) ?? ClaimRow(m.CharId), m, m.CharId == leader, second);
        PlaceRows();
    }

    internal HudRow? RowFor(int memberId)
    {
        foreach (var row in hudRows)
            if (row.MemberId == memberId) return row;
        return null;
    }

    private HudRow ClaimRow(int memberId)
    {
        var row = System.Array.Find(hudRows, r => r.MemberId < 0)!;
        row.MemberId = memberId;
        row.Fresh = true;
        row.TargetY = float.NaN;
        row.Hp.SnapNext();
        row.Mp.SnapNext();
        row.Frame.Visible = true;
        return row;
    }

    private void ReleaseRow(HudRow row)
    {
        if (Drag?.Row == row) Drag = null;
        row.Slide?.Kill();
        row.Slide = null;
        row.MemberId = -1;
        row.TargetY = float.NaN;
        row.Frame.ZIndex = 0;
        row.Frame.Visible = false;
    }

    internal readonly record struct PartyRowView(int Level, int Class, int Hp, int MaxHp, int Mp, int MaxMp,
        Color NameColour, Color HpColour, string Tooltip, bool Cycles, PartyHpLook HpLook, double LevelPercent, int PingMs);

    internal PartyRowView RowOf(PartyMember m, bool leader, long second)
    {
        bool isSelf = m.CharId == context.Self.CharId;
        int hp = isSelf ? self.Vitals.Hp : m.Hp, maxHp = isSelf ? self.Vitals.MaxHp : m.MaxHp;
        int mp = isSelf ? self.Vitals.Mp : m.Mp, maxMp = isSelf ? self.Vitals.MaxMp : m.MaxMp;
        int level = isSelf ? self.Sheet.Level : m.Level, cls = isSelf ? self.Class : m.Class;
        var statuses = party.StatusOf(m.CharId);

        string status = "";
        if (statuses is { Count: > 0 })
        {
            var names = new List<string>();
            foreach (byte s in statuses)
                if (StatusName(s) is { Length: > 0 } name) names.Add(name);
            if (names.Count > 0) status = "\n" + string.Join(", ", names);
        }
        var look = PartyHud.HpLook(statuses, second);
        return new PartyRowView(level, cls, hp, maxHp, mp, maxMp, NameColourOf(m.CharId, leader),
            HpFills[look],
            $"{m.Name}  Lv {level} {CharacterClassCatalog.DisplayName(cls)}\nHP {hp} / {maxHp}   MP {mp} / {maxMp}{status}",
            statuses is { Count: > 1 }, look, LevelPercentOf(m.CharId), PingOf(m.CharId));
    }

    internal int PingOf(int memberId) => memberId == context.Self.CharId ? Game.Connection.PingMs : party.PingOf(memberId);

    internal double LevelPercentOf(int memberId)
    {
        if (memberId == context.Self.CharId) return self.Sheet.ExpPercent;
        return party.LevelPermilleOf(memberId) is { } permille ? PartyHud.LevelPercent(permille) : 0.0;
    }

    private void FillHudRow(HudRow row, PartyMember m, bool leader, long second)
    {
        var view = RowOf(m, leader, second);
        if (view.Cycles) hudCycles = true;

        row.Hp.FillOverride = view.HpLook == PartyHpLook.Normal ? null : view.HpColour;
        row.Hp.Set(view.Hp, view.MaxHp);
        row.Mp.Set(view.Mp, view.MaxMp);
        row.Hp.Visible = view.MaxHp > 0;
        row.Mp.Visible = view.MaxMp > 0;
        string level = $"Lv.{view.Level}";
        if (row.Level.Text != level) row.Level.Text = level;
        if (row.Name.Text != m.Name) row.Name.Text = m.Name;
        row.Name.AddThemeColorOverride("font_color", view.NameColour);
        if (row.Class != view.Class)
        {
            row.Class = view.Class;
            row.Diamond.Icon = UiIcons.Get(PartyHud.ClassIcon(view.Class));
        }
        row.Diamond.SetGauge(view.LevelPercent);
        row.Signal.Ms = view.PingMs;
        row.Frame.TooltipText = view.Tooltip;
        ApplyRowLook(row);
        FillBuffs(row, party.BuffsOf(m.CharId));
    }

    private void ApplyRowLook(HudRow row) =>
        row.Diamond.Highlighted = row.MemberId >= 0 && row.MemberId == Highlight;

    private void FollowHighlight()
    {
        Pick = PartyHud.KeepPick(Pick, context.Selection.SelectedId, pickSelection);
        int highlight = Highlight;
        if (highlight == hudHighlight) return;
        hudHighlight = highlight;
        foreach (var row in hudRows) ApplyRowLook(row);
        context.Plugins.Party();
    }

    private const float RowHeight = BuffTop + BuffIconSize + 2f * RowInsetY;

    private void PlaceRows()
    {
        var orderedRows = new List<HudRow>(hudOrder.Count);
        foreach (int id in hudOrder)
            if (RowFor(id) is { } row) orderedRows.Add(row);
        var lifted = this.Drag is { Lifted: true } drag ? drag.Row : null;
        if (lifted != null && orderedRows.Remove(lifted)) orderedRows.Insert(Mathf.Clamp(this.Drag!.Index, 0, orderedRows.Count), lifted);
        float y = 0f;
        foreach (var row in orderedRows)
        {
            float height = RowHeight;
            row.Frame.Size = new Vector2(UiKit.PartyHudWidth, height);
            if (row != lifted) SlideRow(row, y);
            y += height + RowGap;
        }
        float listHeight = orderedRows.Count > 0 ? y - RowGap : 0f;
        if (Mathf.IsEqualApprox(hudList.CustomMinimumSize.Y, listHeight)) return;
        hudList.CustomMinimumSize = new Vector2(UiKit.PartyHudWidth, listHeight);
        Hud?.ResetSize();
    }

    private static void SlideRow(HudRow row, float y)
    {
        if (Mathf.IsEqualApprox(row.TargetY, y)) return;
        row.TargetY = y;
        row.Slide?.Kill();
        row.Slide = null;
        bool snap = row.Fresh || !row.Frame.IsInsideTree();
        row.Fresh = false;
        if (snap)
        {
            row.Frame.Position = new Vector2(0f, y);
            return;
        }
        row.Slide = row.Frame.CreateTween();
        row.Slide.TweenProperty(row.Frame, "position:y", y, RowSlideSeconds)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    private static void FillBuffs(HudRow row, IReadOnlyList<int> skillIds)
    {
        if (row.BuffIds.SequenceEqual(skillIds)) return;
        row.BuffIds.Clear();
        row.BuffIds.AddRange(skillIds);
        foreach (var icon in row.Buffs.GetChildren())
        {
            row.Buffs.RemoveChild(icon);
            icon.QueueFree();
        }
        float pitch = BuffIconSize + BuffIconGap;
        int perLine = Mathf.Max(1, (int)((RowBodyWidth - BuffLeft + BuffIconGap) / pitch));
        int count = 0;
        foreach (int skillId in skillIds)
        {
            if (count == perLine) break;
            if (SkillData.Get(skillId) is not { } skill) continue;
            var icon = BuffIcon(skill);
            icon.Position = new Vector2(count * pitch, 0f);
            icon.Size = new Vector2(BuffIconSize, BuffIconSize);
            row.Buffs.AddChild(icon);
            count++;
        }
        row.Buffs.Size = new Vector2(RowBodyWidth - BuffLeft, BuffIconSize);
    }

    private static Control BuffIcon(SkillData.Skill skill)
    {
        var size = new Vector2(BuffIconSize, BuffIconSize);
        if (SkillIcons.Get(skill.Id) is { } texture)
            return new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = size,
                MouseFilter = Control.MouseFilterEnum.Pass,
                TooltipText = SkillText.BuffTooltip(skill),
            };

        var letter = UiTheme.Text(skill.Name.Length > 0 ? skill.Name[..1] : "?", BuffLetterFont, NameColour,
            HorizontalAlignment.Center);
        letter.CustomMinimumSize = size;
        letter.VerticalAlignment = VerticalAlignment.Center;
        letter.MouseFilter = Control.MouseFilterEnum.Pass;
        letter.TooltipText = SkillText.BuffTooltip(skill);
        return letter;
    }

    private void RefreshSignals()
    {
        bool changed = false;
        foreach (var row in hudRows)
        {
            if (row.MemberId < 0) continue;
            int ms = PingOf(row.MemberId);
            if (row.Signal.Ms != ms)
            {
                row.Signal.Ms = ms;
                changed = true;
            }
            if (row.MemberId == context.Self.CharId) row.Diamond.SetGauge(self.Sheet.ExpPercent);
        }
        if (changed) context.Plugins.Party();
    }

    internal void HudTick(double now)
    {
        FollowHighlight();
        long second = (long)now;
        if (hudCycles && second != hudSecond) RefreshHud();
        if (second == signalSecond) return;
        signalSecond = second;
        if (party.InParty) RefreshSignals();
    }

    private void OnHudRowInput(HudRow row, InputEvent ev)
    {
        if (row.MemberId < 0) return;
        if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Left } left)
        {
            float y = row.Frame.Position.Y + left.Position.Y;
            if (left.Pressed) Drag = new RowDrag(row, y, left.Position.Y);
            else DropRow(row);
            row.Frame.AcceptEvent();
        }
        else if (ev is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            if (ShowMemberMenu(row.MemberId)) row.Frame.AcceptEvent();
        }
        else if (ev is InputEventMouseMotion motion && Drag?.Row == row)
        {
            DragRow(row.Frame.Position.Y + motion.Position.Y);
            row.Frame.AcceptEvent();
        }
    }

    internal void DragRow(float listY)
    {
        if (this.Drag is not { } drag) return;
        var row = drag.Row;
        if (!drag.Lifted)
        {
            if (Mathf.Abs(listY - drag.PressY) < DragThreshold || hudOrder.Count < 2) return;
            drag.Lifted = true;
            drag.Index = hudOrder.IndexOf(row.MemberId);
            row.Slide?.Kill();
            row.Slide = null;
            row.TargetY = float.NaN;
            row.Frame.ZIndex = LiftedZ;
            ApplyRowLook(row);
        }
        float height = row.Frame.Size.Y;
        float top = Mathf.Clamp(listY - drag.Grab, 0f, Mathf.Max(0f, hudList.CustomMinimumSize.Y - height));
        row.Frame.Position = new Vector2(0f, top);
        int index = PartyOrder.DropIndex(OtherRowCentres(row), top);
        if (index == drag.Index) return;
        drag.Index = index;
        PlaceRows();
    }

    private List<float> OtherRowCentres(HudRow dragged)
    {
        var centres = new List<float>(hudOrder.Count);
        float y = 0f;
        foreach (int id in hudOrder)
        {
            if (RowFor(id) is not { } row || row == dragged) continue;
            float height = RowHeight;
            centres.Add(y + height * 0.5f);
            y += height + RowGap;
        }
        return centres;
    }

    private void DropRow(HudRow row)
    {
        var drag = this.Drag;
        this.Drag = null;
        if (drag == null || drag.Row != row) return;
        if (!drag.Lifted)
        {
            PickMember(row.MemberId);
            return;
        }
        row.Frame.ZIndex = 0;
        order.MoveTo(row.MemberId, drag.Index);
        RefreshHud();
    }

    internal void PickMember(int memberId)
    {
        if (memberId != context.Self.CharId && context.Entities.Views.TryGetValue(memberId, out var member) && context.Selection.Selectable(member))
            context.Selection.Select(memberId);
        else
            context.Selection.Deselect();
        Pick = memberId;
        pickSelection = context.Selection.SelectedId;
        RefreshHud();
    }

    internal void MoveRowTo(int memberId, int index)
    {
        if (order.MoveTo(memberId, index)) RefreshHud();
    }

    private void OpenConfig()
    {
        if (ConfigPopup == null) BuildConfig();
        RefreshConfig();
        var popup = ConfigPopup!;
        popup.ResetSize();
        popup.Position = (Vector2I)ConfigSpot(popup.Size, context.Root.GetViewport().GetVisibleRect().Size);
        popup.Popup();
    }

    internal Vector2 ConfigSpot(Vector2 size, Vector2 screen)
    {
        var gear = gearButton.GetGlobalRect();
        float x = Mathf.Clamp(gear.End.X + ConfigGap, 0f, Mathf.Max(0f, screen.X - size.X));
        float y = Mathf.Clamp(gear.Position.Y, 0f, Mathf.Max(0f, screen.Y - size.Y));
        return new Vector2(x, y);
    }

    internal void BuildConfig()
    {
        var style = UiTheme.WindowPanel(radius: 3, alpha: 1f);
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 8;
        ConfigPopup = new PopupPanel();
        ConfigPopup.AddThemeStyleboxOverride("panel", style);
        hudLayer.AddChild(ConfigPopup);
        var column = new VBoxContainer { CustomMinimumSize = new Vector2(ConfigWidth, 0) };
        column.AddThemeConstantOverride("separation", ConfigSeparation);
        ConfigPopup.AddChild(column);
        column.AddChild(UiTheme.SectionTitle("Party settings"));
        configRejoin = ConfigToggle(column, RejoinLabel, on =>
        {
            if (on != party.Rejoin.On) ToggleRejoin();
        });
        configBoard = ConfigToggle(column, WarBoardLabel, on =>
        {
            if (on != (warBoard?.Panel is { Visible: true })) warBoard?.ToggleWarBoard();
            RefreshConfig();
        });
        configCommander = ConfigToggle(column, CommanderLabel, on =>
        {
            commander?.SetUserHidden(!on);
        });
        var leave = UiTheme.SmallButton(LeaveLabel, "");
        leave.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        leave.Pressed += () =>
        {
            ConfigPopup.Hide();
            AskLeave();
        };
        column.AddChild(leave);
    }

    private static CheckButton ConfigToggle(VBoxContainer column, string text, System.Action<bool> toggled)
    {
        var toggle = new CheckButton { Text = text, FocusMode = Control.FocusModeEnum.None };
        toggle.AddThemeFontSizeOverride("font_size", ConfigFont);
        toggle.AddThemeColorOverride("font_color", UiTheme.TextHi);
        toggle.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        toggle.AddThemeColorOverride("font_pressed_color", UiTheme.TextHi);
        toggle.AddThemeColorOverride("font_hover_pressed_color", UiTheme.GoldBright);
        toggle.AddThemeColorOverride("font_disabled_color", UiTheme.TextDim);
        toggle.Toggled += on => toggled(on);
        column.AddChild(toggle);
        return toggle;
    }

    internal void RefreshConfig()
    {
        if (ConfigPopup == null) return;
        configRejoin.SetPressedNoSignal(party.Rejoin.On);
        configRejoin.TooltipText = RejoinTip;
        configBoard.SetPressedNoSignal(warBoard?.Panel is { Visible: true });
        configCommander.SetPressedNoSignal(commander is { AmCommander: true, UserHidden: false });
        configCommander.Disabled = commander is not { AmCommander: true };
        configCommander.TooltipText = commander is { AmCommander: true } ? "" : CommanderOnlyTip;
    }

    internal bool ShowMemberMenu(int memberId)
    {
        bool vote = context.Zone == TempleZones.JuraidMountain;
        if (memberId == context.Self.CharId || (!AmLeader && !vote)) return false;
        hudMenuMember = memberId;
        hudMenu.Clear();
        if (AmLeader)
        {
            hudMenu.AddItem("Make leader", MenuMakeLeader);
            hudMenu.AddItem("Kick out", MenuKick);
            hudMenu.AddItem("Make commander", MenuCommander);
        }
        if (vote) hudMenu.AddItem(ItemData.Text(UserVote.TitleText, "User Vote"), UserVoteFeature.PartyMenu);
        UiKit.PopupAtCursor(context.Root, hudMenu, context.Root.GetViewport().GetMousePosition());
        return true;
    }

    internal void RunMemberMenu(int memberId, int action)
    {
        hudMenuMember = memberId;
        OnHudMenu(action);
    }

    private void OnHudMenu(long id)
    {
        int member = hudMenuMember;
        hudMenuMember = -1;
        if (member < 0) return;
        if (id == UserVoteFeature.PartyMenu)
        {
            userVoteWindow.Open(member);
            return;
        }
        if (!AmLeader) return;
        string name = party.NameOf(member);
        if (id == MenuMakeLeader)
            Notice.Confirm(context.Root, GameText.System(TextMakeLeader, "Do you authorize %s as a Party Leader?", name),
                "Yes", "No", () => requests.Promote(member), title: "Party");
        else if (id == MenuKick)
            Notice.Confirm(context.Root, GameText.System(TextKick, "Would you like to ban %s from the party?", name),
                "Yes", "No", () => requests.Leave(member), title: "Party");
        else if (id == MenuCommander)
            Notice.Confirm(context.Root, GameText.System(CommanderOrders.AppointText, "Do you authorize %s as a Commander?", name),
                "Yes", "No", () => requests.Commander(member), title: "Party");
    }

    internal void AskLeave()
    {
        if (!party.InParty) return;
        if (AmLeader)
            Notice.Confirm(context.Root, GameText.System(TextDisband, "Are you sure you want to disband the party?"),
                "Yes", "No", () => requests.Disband(), title: "Party");
        else
            Notice.Confirm(context.Root, GameText.System(TextQuit, "Would you like to quit the party?"),
                "Yes", "No", () => requests.Leave(context.Self.CharId), title: "Party");
    }

    internal sealed partial class SignalIcon : Control
    {
        private const int ArcCount = 3;
        private const float ArcFrom = -Mathf.Pi * 0.75f;
        private const float ArcTo = -Mathf.Pi * 0.25f;
        private const int ArcPoints = 12;
        private const float ArcWidth = 1.7f;
        private const float ShadowWidth = 3.2f;
        private const float DotRadius = 1.7f;
        private const float DotShadowGrow = 0.8f;
        private const float BaseInset = 1.5f;
        private const float TopInset = 1f;
        private const float StrikeWidth = 1.6f;
        private static readonly Color Green = new("4cd964");
        private static readonly Color Yellow = new("f2cf3a");
        private static readonly Color Red = new("e8483c");
        private static readonly Color Grey = new(0.56f, 0.56f, 0.56f);
        private static readonly Color Unlit = new(1f, 1f, 1f, 0.16f);
        private static readonly Color Shadow = new(0f, 0f, 0f, 0.45f);
        private static readonly Dictionary<PartySignalLevel, Color> Tints = new()
        {
            [PartySignalLevel.Strong] = Green,
            [PartySignalLevel.Good] = Yellow,
            [PartySignalLevel.Weak] = Red,
            [PartySignalLevel.Poor] = Red,
            [PartySignalLevel.Lost] = Grey,
            [PartySignalLevel.Unknown] = Grey,
        };

        private int pingMs = PartySignal.Unknown;

        public SignalIcon() => TooltipText = PartySignal.Tooltip(pingMs);

        public int Ms
        {
            get => pingMs;
            set
            {
                if (pingMs == value) return;
                pingMs = value;
                TooltipText = PartySignal.Tooltip(value);
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            var level = PartySignal.Level(pingMs);
            int lit = PartySignal.Arcs(level);
            bool lost = level == PartySignalLevel.Lost;
            Color tint = Tints[level];
            var centre = new Vector2(Size.X * 0.5f, Size.Y - DotRadius - BaseInset);
            float step = (centre.Y - TopInset) / ArcCount;
            for (int arc = 1; arc <= ArcCount; arc++)
            {
                float radius = step * arc;
                Color colour = arc <= lit || lost ? tint : Unlit;
                if (arc <= lit || lost) DrawArc(centre, radius, ArcFrom, ArcTo, ArcPoints, Shadow, ShadowWidth, true);
                DrawArc(centre, radius, ArcFrom, ArcTo, ArcPoints, colour, ArcWidth, true);
            }
            DrawCircle(centre, DotRadius + DotShadowGrow, Shadow);
            DrawCircle(centre, DotRadius, tint);
            if (!lost) return;
            var from = new Vector2(TopInset, TopInset);
            var to = new Vector2(Size.X - TopInset, Size.Y - TopInset);
            DrawLine(from, to, Shadow, ShadowWidth, true);
            DrawLine(from, to, tint, StrikeWidth, true);
        }
    }

    private Color NameColourOf(int charId, bool leader) =>
        leader ? LeaderNameColour : charId == party.PartyCommanderId ? PartyCommanderFeature.CommanderNameColour : NameColour;
}
