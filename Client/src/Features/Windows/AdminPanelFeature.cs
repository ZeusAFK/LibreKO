using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AdminPanelFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly AdminSession admin;
    private readonly ShoppingMallSession shoppingMall;
    private readonly SelfSession self;
    private readonly IAdminRequests requests;
    private readonly StatsFeature stats;
    private readonly PlayerSystem player;
    private readonly SkillWindowFeature skillWindow;
    private readonly IChatRequests chatRequests;

    public AdminPanelFeature(IWorldContext context, AdminSession admin, SelfSession self, ShoppingMallSession shoppingMall, IAdminRequests adminRequests, StatsFeature stats, PlayerSystem player, SkillWindowFeature skillWindow, IChatRequests chatRequests)
    {
        this.context = context;
        this.admin = admin;
        this.self = self;
        this.shoppingMall = shoppingMall;
        requests = adminRequests;
        this.stats = stats;
        this.player = player;
        this.skillWindow = skillWindow;
        this.chatRequests = chatRequests;
        CharacterTab = new AdminCharacterTab(requests, self, context.Movement, () => context.Self.IsGm, () => State, SetStatus);
        ClassTab = new AdminClassTab(requests, () => State);
        SkillsTab = new AdminSkillsTab(requests, self, () => State);
        ZonesTab = new AdminZonesTab(requests, () => context.Zone);
        FindTab = new AdminFindTab(requests, () => context.Zone);
        RacesTab = new AdminRacesTab(requests, raceId => requests.RaceRequest(raceId), () => NewEventContent(EventKind.CollectionRace));
        StartEditor = new AdminStartEditor(requests, Confirm);
        EventsPage = new AdminEventsPage(requests, chatRequests, StartEditor, Confirm, EditEventContent);
        LotteriesPage = new AdminLotteriesPage(chatRequests, owner => requests.LotteryRequest(owner.Id), () => NewEventContent(EventKind.Lottery), Confirm);
        CommandsTab = new AdminCommandsTab();
        SpawnEditor = new AdminSpawnEditor(requests);
        TipsTab = new AdminTipsTab(requests, Confirm);
        ItemPicker = new AdminItemPicker(context.Tooltips);
        MonsterPicker = new AdminMonsterPicker();
        LotteryEditor = new AdminLotteryEditor(requests, ItemPicker, context.Tooltips);
        RaceEditor = new AdminRaceEditor(requests, ItemPicker, MonsterPicker, context.Tooltips);
        GroupPicker = new AdminGroupPicker(requests, context.Tooltips);
        ItemGroupEditor = new AdminItemGroupEditor(requests, context.Tooltips, ItemPicker, Confirm);
        MonsterPage = new AdminMonsterPage(requests, context.Tooltips, ItemPicker, GroupPicker, ItemGroupEditor.Request, Confirm);
        PlayersPage = new AdminPlayersPage(requests, chatRequests, SetTarget);
        PlayerPage = new AdminPlayerPage(chatRequests, context.Root, Confirm, RefreshTarget, () => ShowPage(GmPanelPages.Inventory));
        InventoryPage = new AdminInventoryPage(requests, context.Tooltips, ItemPicker, Confirm);
        WorldControlPage = new AdminWorldControlPage(chatRequests, Confirm);
        ServerToolsPage = new AdminServerToolsPage(chatRequests, Confirm);
    }

    internal const float PanelWidth = 1160;
    internal const float PanelHeight = 700;
    private const float ScreenMargin = 48;
    private const int PageTitleSize = 18;
    private const string FailureTitle = "Game Master";

    internal AdminCharacterTab CharacterTab { get; }
    internal AdminClassTab ClassTab { get; }
    internal AdminSkillsTab SkillsTab { get; }
    internal AdminZonesTab ZonesTab { get; }
    internal AdminFindTab FindTab { get; }
    internal AdminRacesTab RacesTab { get; }
    internal AdminEventsPage EventsPage { get; }
    internal AdminStartEditor StartEditor { get; }
    internal AdminLotteriesPage LotteriesPage { get; }
    internal AdminCommandsTab CommandsTab { get; }
    internal AdminSpawnEditor SpawnEditor { get; }
    internal AdminItemGroupEditor ItemGroupEditor { get; }
    internal AdminGroupPicker GroupPicker { get; }
    internal AdminMonsterPage MonsterPage { get; }
    internal AdminLotteryEditor LotteryEditor { get; }
    internal AdminRaceEditor RaceEditor { get; }
    internal AdminTipsTab TipsTab { get; }
    internal AdminItemPicker ItemPicker { get; }
    internal AdminMonsterPicker MonsterPicker { get; }
    internal AdminPlayersPage PlayersPage { get; }
    internal AdminPlayerPage PlayerPage { get; }
    internal AdminInventoryPage InventoryPage { get; }
    internal AdminWorldControlPage WorldControlPage { get; }
    internal AdminServerToolsPage ServerToolsPage { get; }

    private CanvasLayer layer = null!;
    internal HudWindow Window { get; private set; } = null!;
    internal bool Enabled { get; set; }
    internal bool Shown { get; set; }
    private AdminPanelGrant grant;
    internal AdminState State { get; set; }
    private readonly Dictionary<string, Control> pages = new();

    private AdminNavRail rail = null!;
    private TextureRect pageIcon = null!;
    private Label pageTitle = null!;
    private MarginContainer pageHost = null!;
    private ScrollContainer pageScroll = null!;
    private MarginContainer fillHost = null!;
    private Control pagePark = null!;
    private Notice? failure;
    internal string CurrentPage { get; private set; } = "";
    private AdminTargetBar targetBar = null!;
    private int targetId;
    internal AdminPlayerSheet? Target { get; private set; }
    private PlayerFindFor playerFindFor;
    private string manageName = "";

    private enum PlayerFindFor { Page, Bar, Manage }

    public void Build()
    {
        player.CollisionsChanged += CharacterTab.RefreshCollisionSwitch;
        skillWindow.SkillsRebuilt += SkillsTab.LoadSkillSpins;
        admin.AdminGrantEvent += OnGrant;
        shoppingMall.ShoppingMallBalanceEvent += CharacterTab.ShowCash;
        admin.AdminStateEvent += OnState;
        admin.AdminResultEvent += OnResult;
        admin.AdminCollectionRacesEvent += RacesTab.OnCollectionRaces;
        admin.AdminFindEvent += OnFind;
        admin.AdminPlayerSheetEvent += OnPlayerSheet;
        admin.AdminSpawnRowEvent += OnSpawnRow;
        admin.AdminMonsterSheetEvent += OnMonsterSheet;
        admin.AdminItemGroupEvent += OnItemGroup;
        admin.AdminItemGroupsEvent += GroupPicker.OnGroups;
        admin.AdminDropSimulationEvent += OnDropSimulation;
        admin.AdminEventSchedulesEvent += OnEventSchedules;
        admin.AdminLotteryEvent += OnLottery;
        admin.AdminRaceEvent += OnRace;
        admin.AdminTipsEvent += TipsTab.OnTips;
        admin.AdminCommandsEvent += OnCommands;
        Enable(context.Self.IsGm ? AdminPanelGrant.GameMaster : admin.PanelGrant);
    }

    private void OnGrant(AdminPanelGrant grant) => Enable(grant);

    private bool FullPanel => context.Self.IsGm || grant == AdminPanelGrant.GameMaster;

    private void Enable(AdminPanelGrant grant)
    {
        if (Enabled || grant == AdminPanelGrant.None) return;
        Enabled = true;
        this.grant = grant;
        SeedState();
        BuildAdminPanel();
    }

    private void SeedState()
    {
        var info = self.LastEnter;
        State = new AdminState
        {
            Granted = true,
            Class = info.Class, Level = self.Sheet.Level,
            Str = self.Sheet.Str, Sta = self.Sheet.Sta, Dex = self.Sheet.Dex,
            Intel = self.Sheet.Intel, MagicStat = self.Sheet.Mag,
            StatPoints = self.Sheet.Points,
            MaxHp = self.Vitals.MaxHp, MaxMp = self.Vitals.MaxMp,
            Ap = self.Sheet.Ap, Ac = self.Sheet.Ac, Gold = self.Sheet.Gold,
            Loyalty = self.Sheet.Np,
            SkillPoints = self.Mastery.ToArray(),
            ClassOptions = System.Array.Empty<int>(),
            Nation = info.Nation, Race = info.Race,
        };
    }

    bool IEscapable.CloseOnEscape()
    {
        if (Shown) Close();
        else return false;
        return true;
    }

    internal IEscapable SpawnPanel => new ClosablePanel(() => SpawnEditor.Shown, SpawnEditor.Close);
    internal IEscapable GroupPickerPanel => new ClosablePanel(() => GroupPicker.Shown, GroupPicker.Close);
    internal IEscapable ItemGroupPanel => new ClosablePanel(() => ItemGroupEditor.Shown, ItemGroupEditor.Close);
    internal IEscapable LotteryPanel => new ClosablePanel(() => LotteryEditor.Shown, LotteryEditor.Close);
    internal IEscapable RacePanel => new ClosablePanel(() => RaceEditor.Shown, RaceEditor.Close);
    internal IEscapable PickerPanel => new ClosablePanel(() => ItemPicker.Shown, ItemPicker.Close);
    internal IEscapable MonsterPickerPanel => new ClosablePanel(() => MonsterPicker.Shown, MonsterPicker.Close);
    internal IEscapable StartPanel => new ClosablePanel(() => StartEditor.Shown, StartEditor.Close);
    internal IEscapable TipPanel => new ClosablePanel(() => TipsTab.EditorShown, TipsTab.CloseEditor);

    public void Dispose()
    {
        player.CollisionsChanged -= CharacterTab.RefreshCollisionSwitch;
        skillWindow.SkillsRebuilt -= SkillsTab.LoadSkillSpins;
        admin.AdminGrantEvent -= OnGrant;
        shoppingMall.ShoppingMallBalanceEvent -= CharacterTab.ShowCash;
        admin.AdminStateEvent -= OnState;
        admin.AdminResultEvent -= OnResult;
        admin.AdminCollectionRacesEvent -= RacesTab.OnCollectionRaces;
        admin.AdminFindEvent -= OnFind;
        admin.AdminPlayerSheetEvent -= OnPlayerSheet;
        admin.AdminSpawnRowEvent -= OnSpawnRow;
        admin.AdminMonsterSheetEvent -= OnMonsterSheet;
        admin.AdminItemGroupEvent -= OnItemGroup;
        admin.AdminItemGroupsEvent -= GroupPicker.OnGroups;
        admin.AdminDropSimulationEvent -= OnDropSimulation;
        admin.AdminEventSchedulesEvent -= OnEventSchedules;
        admin.AdminLotteryEvent -= OnLottery;
        admin.AdminRaceEvent -= OnRace;
        admin.AdminTipsEvent -= TipsTab.OnTips;
        admin.AdminCommandsEvent -= OnCommands;
    }

    internal void BuildAdminPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Window = new HudWindow(
            "admin_panel", "Game Master", new Vector2(60, 40),
            titleIcon: UiIcons.Get("system/lock")) { Visible = false };
        Window.Closed += Close;
        layer.AddChild(Window);

        var frame = new HBoxContainer { CustomMinimumSize = PanelSize() };
        frame.AddThemeConstantOverride("separation", 12);
        Window.Body.AddChild(frame);

        var content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        content.AddThemeConstantOverride("separation", 8);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        content.AddChild(header);
        pageIcon = new TextureRect
        {
            CustomMinimumSize = new Vector2(PageTitleSize + 4, PageTitleSize + 4),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SelfModulate = UiTheme.GoldVivid,
        };
        header.AddChild(pageIcon);
        pageTitle = UiTheme.Text("", PageTitleSize, UiTheme.GoldVivid);
        pageTitle.AddThemeFontOverride("font", UiTheme.Strong);
        header.AddChild(pageTitle);
        content.AddChild(UiTheme.Rule());

        targetBar = new AdminTargetBar { Visible = false };
        targetBar.SearchRequested += text =>
        {
            playerFindFor = PlayerFindFor.Bar;
            requests.Find((int)AdminFindKind.Players, text);
        };
        targetBar.TargetPicked += SetTarget;
        targetBar.Cleared += ClearTarget;
        content.AddChild(targetBar);

        pageScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        content.AddChild(pageScroll);
        pageHost = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        pageHost.AddThemeConstantOverride("margin_right", 10);
        pageScroll.AddChild(pageHost);
        fillHost = new MarginContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, Visible = false };
        content.AddChild(fillHost);
        pagePark = new Control { Visible = false };
        content.AddChild(pagePark);

        AddPage(GmPanelPages.SelfCharacter, CharacterTab.Build());
        AddPage(GmPanelPages.SelfItems, BuildItemsTab());
        AddPage(GmPanelPages.SelfClass, ClassTab.Build());
        AddPage(GmPanelPages.SelfSkills, SkillsTab.Build());
        AddPage(GmPanelPages.Travel, ZonesTab.Build());
        if (FullPanel)
        {
            AddPage(GmPanelPages.Find, PlayersPage.Build());
            AddPage(GmPanelPages.Player, PlayerPage.Build());
            AddPage(GmPanelPages.Inventory, InventoryPage.Build());
            AddPage(GmPanelPages.Monsters, FindTab.Build());
            AddPage(GmPanelPages.Monster, MonsterPage.Build());
            AddPage(GmPanelPages.Calendar, EventsPage.Build());
            AddPage(GmPanelPages.Lotteries, LotteriesPage.Build());
            AddPage(GmPanelPages.Races, RacesTab.Build());
            AddPage(GmPanelPages.Tips, TipsTab.Build());
            AddPage(GmPanelPages.WorldControl, WorldControlPage.Build());
            AddPage(GmPanelPages.Commands, CommandsTab.Build());
            AddPage(GmPanelPages.Tools, ServerToolsPage.Build());
            requests.CommandsRequest();
        }

        rail = new AdminNavRail(GmPanelPages.For(FullPanel).Where(page => pages.ContainsKey(page.Id)).ToList());
        rail.PageChosen += ShowPage;
        frame.AddChild(rail);
        frame.AddChild(UiTheme.Rule(vertical: true));
        frame.AddChild(content);

        SkillsTab.LoadSkillSpins();
        CharacterTab.Refresh();
        ClassTab.Refresh();
        ClassTab.SyncLookPicks();
        ZonesTab.Refresh();
        if (FullPanel)
        {
            ItemPicker.Build(layer);
            MonsterPicker.Build(layer);
            GroupPicker.Build(layer);
            SpawnEditor.Build(layer);
            ItemGroupEditor.Build(layer);
            LotteryEditor.Build(layer);
            RaceEditor.Build(layer);
            StartEditor.Build(layer);
            TipsTab.BuildEditor(layer);
        }
        ShowPage(GmPanelPages.FirstPage(FullPanel));
    }

    private Vector2 PanelSize()
    {
        if (context.Root.GetViewport() is not { } viewport) return new Vector2(PanelWidth, PanelHeight);
        var screen = viewport.GetVisibleRect().Size;
        return new Vector2(Mathf.Min(PanelWidth, screen.X - ScreenMargin), Mathf.Min(PanelHeight, screen.Y - ScreenMargin));
    }

    private void AddPage(string id, Control body)
    {
        body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pagePark.AddChild(body);
        pages[id] = body;
    }

    internal void ShowPage(string id)
    {
        if (!pages.TryGetValue(id, out Control? next) || GmPanelPages.Get(id) is not { } page) return;
        var host = page.FillsHeight ? fillHost : pageHost;
        foreach (var (key, body) in pages)
        {
            var wanted = key == id ? (Node)host : pagePark;
            if (body.GetParent() == wanted) continue;
            body.GetParent()?.RemoveChild(body);
            wanted.AddChild(body);
        }
        pageScroll.Visible = !page.FillsHeight;
        fillHost.Visible = page.FillsHeight;
        next.Visible = true;
        CurrentPage = id;
        pageTitle.Text = page.Title;
        pageIcon.Texture = UiIcons.Get(page.Icon);
        rail.Select(id);
        context.Tooltips.Hide();
        targetBar.Visible = page.UsesTarget;
        if (page.UsesTarget && targetId == 0) AdminPanelUi.FocusLater(targetBar.Query);
        if (page.UsesTarget && targetId != 0) requests.PlayerSheetRequest(targetId);

        switch (id)
        {
            case GmPanelPages.Travel: ZonesTab.Refresh(); break;
            case GmPanelPages.Find: AdminPanelUi.FocusLater(PlayersPage.Query); break;
            case GmPanelPages.Monsters: AdminPanelUi.FocusLater(FindTab.FindQuery); break;
            case GmPanelPages.SelfSkills: SkillsTab.LoadSkillSpins(); break;
            case GmPanelPages.SelfClass: ClassTab.SyncLookPicks(); break;
            case GmPanelPages.Races: requests.CollectionRacesRequest(); break;
            case GmPanelPages.Calendar:
            case GmPanelPages.Lotteries:
                EventsPage.Request();
                break;
            case GmPanelPages.Tips: requests.TipsRequest(); break;
            case GmPanelPages.SelfItems when !itemsLoaded:
                itemsLoaded = true;
                ItemSearch.Refresh();
                break;
        }
    }

    internal bool CanManagePlayers => Enabled && FullPanel;

    internal void ManagePlayer(string name)
    {
        if (!CanManagePlayers) return;
        if (!Shown) Toggle();
        manageName = name;
        playerFindFor = PlayerFindFor.Manage;
        requests.Find((int)AdminFindKind.Players, name);
    }

    private void OnFind(int kind, int total, List<AdminFindHit> hits)
    {
        if ((AdminFindKind)kind != AdminFindKind.Players)
        {
            FindTab.OnFind(kind, total, hits);
            return;
        }
        switch (playerFindFor)
        {
            case PlayerFindFor.Bar:
                targetBar.ShowSuggestions(hits);
                break;
            case PlayerFindFor.Manage:
                playerFindFor = PlayerFindFor.Page;
                if (hits.FirstOrDefault(hit => string.Equals(hit.Name, manageName, StringComparison.OrdinalIgnoreCase)) is { } exact)
                    SetTarget(exact);
                break;
            default:
                PlayersPage.OnFind(total, hits);
                break;
        }
    }

    internal void SetTarget(AdminFindHit hit)
    {
        targetId = hit.Id;
        requests.PlayerSheetRequest(hit.Id);
        if (GmPanelPages.Get(CurrentPage) is not { UsesTarget: true }) ShowPage(GmPanelPages.Player);
    }

    private void RefreshTarget()
    {
        if (targetId != 0) requests.PlayerSheetRequest(targetId);
    }

    private void ClearTarget()
    {
        targetId = 0;
        Target = null;
        targetBar.ShowTarget(null);
        PlayerPage.Show(null);
        InventoryPage.Show(null);
    }

    internal void OnPlayerSheet(AdminPlayerSheet sheet)
    {
        if (!Enabled || sheet.Id != targetId) return;
        Target = sheet;
        targetBar.ShowTarget(sheet);
        PlayerPage.Show(sheet);
        InventoryPage.Show(sheet);
    }

    private void Confirm(string question, string confirmText, Action action) =>
        Notice.Confirm(context.Root, question, confirmText, "Cancel", action, title: FailureTitle);

    internal void Toggle()
    {
        if (!Enabled) return;
        if (Shown) { Close(); return; }
        Shown = true;
        Window.Visible = true;
        Window.GetParent()?.MoveChild(Window, Window.GetParent().GetChildCount() - 1);
        CharacterTab.RefreshCash();
        requests.StateRequest();
        if (CurrentPage.Length > 0) ShowPage(CurrentPage);
    }

    internal void Close()
    {
        if (!Shown) return;
        Shown = false;
        Window.Visible = false;
        context.Tooltips.Hide();
    }

    private void OnState(AdminState state)
    {
        if (!Enabled) return;
        if (!state.Granted)
        {
            Close();
            ShowFailure("The server refused the GM panel for this account.");
            return;
        }

        int previousClass = State.Class;
        State = state;

        context.Hud.RefreshVitalBars();
        skillWindow.RefreshSkillsEnabled();
        if (state.Class != previousClass) stats.ApplyClassChange(state.Class);
        else stats.RefreshUI();

        SkillsTab.LoadSkillSpins();
        CharacterTab.Refresh();
        ClassTab.Refresh();
        if (state.Class != previousClass && itemsLoaded) ItemSearch.Refresh();
    }

    private void OnResult(bool ok, string message)
    {
        if (!Enabled || ok) return;
        ShowFailure(message);
    }

    internal void ShowFailure(string message)
    {
        if (message.Length == 0) return;
        if (failure != null && GodotObject.IsInstanceValid(failure))
        {
            failure.SetMessage(message);
            return;
        }
        failure = Notice.Show(context.Root, message, FailureTitle, () => failure = null);
    }

    internal void OnMonsterSheet(AdminMonsterSheet sheet)
    {
        if (!FullPanel || !Enabled) return;
        MonsterPage.Show(sheet);
        if (!Shown) Toggle();
        if (CurrentPage != GmPanelPages.Monster) ShowPage(GmPanelPages.Monster);
    }

    internal void OnItemGroup(AdminItemGroup group)
    {
        if (!FullPanel || !Enabled) return;
        MonsterPage.OnGroup(group);
        ItemGroupEditor.Show(group);
    }

    internal void OnEventSchedules(AdminEventSchedules schedules)
    {
        if (!FullPanel || !Enabled) return;
        if (EventsPage.Built) EventsPage.OnSchedules(schedules);
        LotteriesPage.OnSchedules(schedules);
        if (LotteryEditor.Shown && LotteryEditor.LotteryId > 0 && !schedules.Owners.Any(o => o.Kind == EventKind.Lottery && o.Id == LotteryEditor.LotteryId))
            LotteryEditor.Close();
        if (RaceEditor.Shown && RaceEditor.RaceId > 0 && !schedules.Owners.Any(o => o.Kind == EventKind.CollectionRace && o.Id == RaceEditor.RaceId))
            RaceEditor.Close();
    }

    private void OnLottery(AdminLottery lottery)
    {
        if (FullPanel && Enabled) LotteryEditor.Show(lottery);
    }

    private void OnRace(AdminRace race)
    {
        if (FullPanel && Enabled) RaceEditor.Show(race);
    }

    private void EditEventContent(EventOwner owner)
    {
        if (owner.Kind == EventKind.Lottery) requests.LotteryRequest(owner.Id);
        else if (owner.Kind == EventKind.CollectionRace) requests.RaceRequest(owner.Id);
    }

    private void NewEventContent(EventKind kind)
    {
        if (kind == EventKind.Lottery) LotteryEditor.Show(new AdminLottery());
        else RaceEditor.Show(new AdminRace { ZoneId = context.Zone });
    }

    private void OnDropSimulation(AdminDropSimulation result)
    {
        if (FullPanel && Enabled) MonsterPage.ShowSimulation(result);
    }

    internal void OnSpawnRow(AdminSpawnRow row)
    {
        if (!FullPanel) return;
        SpawnEditor.Show(row);
    }

    internal void OnCommands(List<GmCommandInfo> commands, bool open)
    {
        CommandsTab.SetCommands(commands);
        if (!Enabled || !CommandsTab.Built) return;
        if (!open) return;
        if (!Shown) Toggle();
        ShowPage(GmPanelPages.Commands);
    }

    internal void SetStatus(string text, bool warn)
    {
        if (warn) ShowFailure(text);
    }

    internal ItemSearchPanel ItemSearch { get; private set; } = null!;
    private bool itemsLoaded;

    private Control BuildItemsTab()
    {
        ItemSearch = new ItemSearchPanel(
            tradeableOnly: false,
            actionText: "Add",
            onAction: (hit, count) => OnGiveItem(hit.Id, count),
            showTooltip: itemId => context.Tooltips.Show(-1, UiKit.TooltipItem(itemId)),
            hideTooltip: context.Tooltips.Hide);
        ItemSearch.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return ItemSearch;
    }

    private void OnGiveItem(int itemId, int count) => requests.GiveItem(itemId, count);
}
