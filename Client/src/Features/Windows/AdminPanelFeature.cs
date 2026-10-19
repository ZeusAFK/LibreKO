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

    public AdminPanelFeature(IWorldContext context, AdminSession admin, SelfSession self, ShoppingMallSession shoppingMall, IAdminRequests adminRequests, StatsFeature stats, PlayerSystem player, SkillWindowFeature skillWindow)
    {
        this.context = context;
        this.admin = admin;
        this.self = self;
        this.shoppingMall = shoppingMall;
        requests = adminRequests;
        this.stats = stats;
        this.player = player;
        this.skillWindow = skillWindow;
        CharacterTab = new AdminCharacterTab(requests, self, context.Movement, () => context.Self.IsGm, () => State, SetStatus);
        ClassTab = new AdminClassTab(requests, () => State, SetStatus);
        SkillsTab = new AdminSkillsTab(requests, self, () => State, SetStatus);
        ZonesTab = new AdminZonesTab(requests, () => context.Zone, SetStatus);
        FindTab = new AdminFindTab(requests, () => context.Zone, SetStatus);
        RacesTab = new AdminRacesTab(requests);
        CommandsTab = new AdminCommandsTab();
        SpawnEditor = new AdminSpawnEditor(requests, SetStatus);
    }

    internal const string CommandsLabel = "Commands";
    private const string CharacterLabel = "Character";
    private const string ItemsLabel = "Items";
    private const string ClassLabel = "Class";
    private const string SkillsLabel = "Skills";
    private const string ZonesLabel = "Zones";
    private const string FindLabel = "Find";
    private const string RacesLabel = "Races";

    internal AdminCharacterTab CharacterTab { get; }
    internal AdminClassTab ClassTab { get; }
    internal AdminSkillsTab SkillsTab { get; }
    internal AdminZonesTab ZonesTab { get; }
    internal AdminFindTab FindTab { get; }
    internal AdminRacesTab RacesTab { get; }
    internal AdminCommandsTab CommandsTab { get; }
    internal AdminSpawnEditor SpawnEditor { get; }

    private CanvasLayer layer = null!;
    internal HudWindow Window { get; private set; } = null!;
    internal bool Enabled { get; set; }
    internal bool Shown { get; set; }
    private AdminPanelGrant grant;
    internal AdminState State { get; set; }
    private readonly Dictionary<string, Control> tabs = new();

    private readonly Dictionary<string, Button> tabBtns = new();

    private MarginContainer tabHost = null!;
    private Control tabPark = null!;
    private Label statusLbl = null!;

    public void Build()
    {
        player.CollisionsChanged += CharacterTab.RefreshCollisionSwitch;
        skillWindow.SkillsRebuilt += SkillsTab.LoadSkillSpins;
        admin.AdminGrantEvent += OnGrant;
        shoppingMall.ShoppingMallBalanceEvent += CharacterTab.ShowCash;
        admin.AdminStateEvent += OnState;
        admin.AdminResultEvent += OnResult;
        admin.AdminCollectionRacesEvent += RacesTab.OnCollectionRaces;
        admin.AdminFindEvent += FindTab.OnFind;
        admin.AdminSpawnRowEvent += OnSpawnRow;
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

    public void Dispose()
    {
        player.CollisionsChanged -= CharacterTab.RefreshCollisionSwitch;
        skillWindow.SkillsRebuilt -= SkillsTab.LoadSkillSpins;
        admin.AdminGrantEvent -= OnGrant;
        shoppingMall.ShoppingMallBalanceEvent -= CharacterTab.ShowCash;
        admin.AdminStateEvent -= OnState;
        admin.AdminResultEvent -= OnResult;
        admin.AdminCollectionRacesEvent -= RacesTab.OnCollectionRaces;
        admin.AdminFindEvent -= FindTab.OnFind;
        admin.AdminSpawnRowEvent -= OnSpawnRow;
        admin.AdminCommandsEvent -= OnCommands;
    }

    internal void BuildAdminPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);

        Window = new HudWindow(
            "admin_panel", "Game Master", new Vector2(150, 70),
            titleIcon: UiIcons.Get("system/lock")) { Visible = false };
        Window.Closed += Close;
        layer.AddChild(Window);

        var root = Window.Body;
        root.AddThemeConstantOverride("separation", 9);

        var tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", 5);
        root.AddChild(tabBar);
        root.AddChild(UiTheme.Rule());

        tabHost = new MarginContainer();
        root.AddChild(tabHost);
        tabPark = new Control { Visible = false };
        root.AddChild(tabPark);

        AddTab(tabBar, CharacterLabel, CharacterTab.Build());
        AddTab(tabBar, ItemsLabel, BuildItemsTab());
        AddTab(tabBar, ClassLabel, ClassTab.Build());
        AddTab(tabBar, SkillsLabel, SkillsTab.Build());
        AddTab(tabBar, ZonesLabel, ZonesTab.Build());
        if (FullPanel)
        {
            AddTab(tabBar, FindLabel, FindTab.Build());
            AddTab(tabBar, RacesLabel, RacesTab.Build());
            AddTab(tabBar, CommandsLabel, CommandsTab.Build());
            requests.CommandsRequest();
        }

        statusLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        statusLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        statusLbl.CustomMinimumSize = new Vector2(AdminPanelUi.TabWidth, 0);
        root.AddChild(statusLbl);

        SkillsTab.LoadSkillSpins();
        CharacterTab.Refresh();
        ClassTab.Refresh();
        ClassTab.SyncLookPicks();
        ZonesTab.Refresh();
        if (FullPanel) SpawnEditor.Build(layer);
        SelectTab(CharacterLabel);
        Callable.From(Window.ResetSize).CallDeferred();
    }

    private void AddTab(HBoxContainer tabBar, string label, Control body)
    {
        tabPark.AddChild(body);
        tabs[label] = body;
        var button = UiTheme.UnderlineTabButton(label);
        button.Pressed += () => SelectTab(label);
        tabBtns[label] = button;
        tabBar.AddChild(button);
    }

    internal void SelectTab(string label)
    {
        if (!tabs.TryGetValue(label, out Control? next)) return;

        foreach (var (key, body) in tabs)
        {
            var wanted = key == label ? (Node)tabHost : tabPark;
            if (body.GetParent() == wanted) continue;
            body.GetParent()?.RemoveChild(body);
            wanted.AddChild(body);
        }
        next.Visible = true;
        foreach (var (key, button) in tabBtns) button.ButtonPressed = key == label;
        Callable.From(Window.ResetSize).CallDeferred();

        if (label == ZonesLabel) ZonesTab.Refresh();
        if (label == FindLabel) Callable.From(FindTab.FindQuery.GrabFocus).CallDeferred();
        if (label == SkillsLabel) SkillsTab.LoadSkillSpins();
        if (label == ClassLabel) ClassTab.SyncLookPicks();
        if (label == RacesLabel) requests.CollectionRacesRequest();
        if (label != ItemsLabel) { context.Tooltips.Hide(); return; }
        if (itemsLoaded) return;
        itemsLoaded = true;
        ItemSearch.Refresh();
    }

    internal void Toggle()
    {
        if (!Enabled) return;
        if (Shown) { Close(); return; }
        Shown = true;
        Window.Visible = true;
        Window.GetParent()?.MoveChild(Window, Window.GetParent().GetChildCount() - 1);
        SetStatus("", false);
        CharacterTab.RefreshCash();
        requests.StateRequest();
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
            context.Log.Notice("The server refused the GM panel for this account.");
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
        if (!Enabled) return;
        SetStatus(message, !ok);
        if (!ok) context.Log.Notice(message);
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
        SelectTab(CommandsLabel);
    }

    internal void SetStatus(string text, bool warn)
    {
        if (statusLbl == null) return;
        statusLbl.Text = text;
        statusLbl.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
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
        return ItemSearch;
    }

    private void OnGiveItem(int itemId, int count)
    {
        requests.GiveItem(itemId, count);
        SetStatus($"Requesting {ItemData.DisplayName(itemId)} x{count}…", false);
    }
}
