using Godot;
using LibreKO.Scene;

namespace LibreKO.Features.Windows;

internal sealed class MainWindowsFeature : IFeature, IMainWindows, IEscapable
{
    private readonly IWorldContext context;
    private readonly BagCompanionFeature bagCompanions;
    private readonly WorldRoot world;
    private readonly SelfSession self;

    public MainWindowsFeature(IWorldContext context, WorldRoot world, SelfSession self, BagCompanionFeature bagCompanions)
    {
        this.context = context;
        this.world = world;
        this.self = self;
        this.bagCompanions = bagCompanions;
    }

    public void Build()
    {
        BuildPanel();
        BuildDock();
    }

    bool IEscapable.CloseOnEscape()
    {
        if (AnyOpen) CloseAll();
        else return false;
        return true;
    }

    public void Dispose()
    {
        if (context.Root.IsInsideTree()) context.Root.GetViewport().SizeChanged -= QueueDockLayout;
    }

    private CanvasLayer layer = null!;

    internal Dictionary<string, HudWindow> Windows { get; } = new();

    private bool shown;

    internal bool AnyOpen => shown;

    private readonly Dictionary<CharacterPage, Button> characterPageTabs = new();

    private readonly Dictionary<CharacterPage, Control> characterPages = new();

    private Control BuildCharacterPages()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 2);
        root.AddChild(tabs);

        characterPages[CharacterPage.Character] = world.Stats.Content;
        characterPages[CharacterPage.Clan] = world.Clan.Content;
        characterPages[CharacterPage.Friends] = world.Friends.Content;

        foreach (var (page, content) in characterPages)
        {
            var which = page;
            var button = UiTheme.TopTabButton(CharacterPageName(page), 12);
            button.Pressed += () => ShowCharacterPage(which);
            characterPageTabs[page] = button;
            tabs.AddChild(button);
            root.AddChild(content);
        }

        ShowCharacterPage(CharacterPage.Character);
        return root;
    }

    internal void ShowCharacterPage(CharacterPage page)
    {
        foreach (var (key, content) in characterPages) content.Visible = key == page;
        foreach (var (key, button) in characterPageTabs) button.ButtonPressed = key == page;
        if (page == CharacterPage.Friends) world.Friends.EnsureLoaded();
        if (page == CharacterPage.Clan) world.Clan.EnsureLoaded();
        if (Windows.TryGetValue("Character", out HudWindow? window))
            Callable.From(window.ResetSize).CallDeferred();
    }

    private static string CharacterPageName(CharacterPage page) => page switch
    {
        CharacterPage.Character => "Character",
        CharacterPage.Clan => "Clan",
        _ => "Friends",
    };

    public void OpenCharacterPage(CharacterPage page)
    {
        if (!Windows.TryGetValue("Character", out HudWindow? window)) return;
        if (!window.Visible || characterPageTabs[page].ButtonPressed == false)
        {
            if (!window.Visible) Toggle("Character");
            ShowCharacterPage(page);
            return;
        }
        Toggle("Character");
    }

    public HudWindow? Window(string key) => Windows.TryGetValue(key, out var window) ? window : null;

    public HudWindow? SkillsWindow() => Window("Skills");

    public bool CharTabOpen() => IsOpen("Inventory");

    internal void BuildPanel()
    {
        layer = new CanvasLayer { Layer = UiLayers.MainWindows };
        context.Root.AddChild(layer);

        Add(
            "Character", "character_info", "Character Info", new Vector2(64, 82),
            BuildCharacterPages(), UiKit.CharacterPageWidth, UiIcons.Get("game/helmet"));
        Add(
            "Inventory", "inventory", "INVENTORY", new Vector2(334, 66),
            world.InventoryWindow.InvContent, 0, UiIcons.Get("system/bag")).SetBackgroundAlpha(UiTheme.TranslucentWindowAlpha);
        Windows["Inventory"].Layout.Placed += bagCompanions.Pairing.PlayerTouched;
        Add(
            "Skills", "skills", "Skills", new Vector2(120, 84),
            world.SkillWindow.SkillsContent, 330, UiIcons.Get("game/main-hand"));
        Add(
            "Quests", "quests", "QUESTS", new Vector2(170, 86),
            world.Quest.QuestsContent, 420, UiIcons.Get("system/scroll"));

        RestoreWindows();
    }

    private void RestoreWindows()
    {
        foreach (var (key, window) in Windows)
        {
            if (!self.IsWindowOpen(key)) continue;
            window.Visible = true;
            Refresh(key);
        }
        SyncState();
    }

    private HudWindow Add(
        string key,
        string layoutId,
        string title,
        Vector2 defaultPosition,
        Control content,
        int bodyMinWidth,
        Texture2D? icon)
    {
        var window = new HudWindow(
            layoutId,
            title,
            defaultPosition,
            bodyMinWidth,
            persistLayout: true,
            titleIcon: icon)
        {
            Visible = false,
        };
        window.Closed += SyncState;
        if (LeftDocked.Contains(key) || RightDocked.Contains(key)) window.ShowDockPin(() => Redock(key));
        window.Body.AddChild(content);
        layer.AddChild(window);
        Callable.From(window.ResetSize).CallDeferred();
        Windows[key] = window;
        return window;
    }

    public bool IsOpen(string key) =>
        Windows.TryGetValue(key, out HudWindow? window) && window.Visible;

    public void Toggle(string key)
    {
        if (key == "Inventory") bagCompanions.Pairing.PlayerTouched();
        if (!Windows.TryGetValue(key, out HudWindow? window)) return;
        if (window.Visible)
        {
            window.Visible = false;
            if (key == "Inventory") { context.Tooltips.Hide(); world.InventoryWindow.HideDeletePrompt(); }
            Audio.PlayUi(Sfx.InventoryClose);
        }
        else
        {
            window.Visible = true;
            window.GetParent()?.MoveChild(window, window.GetParent().GetChildCount() - 1);
            Refresh(key);
            Audio.PlayUi(Sfx.InventoryOpen);
        }
        SyncState();
    }

    public void Hide(string key)
    {
        if (!Windows.TryGetValue(key, out HudWindow? window) || !window.Visible) return;
        window.Visible = false;
        if (key == "Inventory") { context.Tooltips.Hide(); world.InventoryWindow.HideDeletePrompt(); }
        SyncState();
    }

    public void Show(string key)
    {
        if (!Windows.TryGetValue(key, out HudWindow? window)) return;
        window.Visible = true;
        window.GetParent()?.MoveChild(window, window.GetParent().GetChildCount() - 1);
        Refresh(key);
        SyncState();
    }

    internal void Refresh(string key)
    {
        switch (key)
        {
            case "Character":
                world.Stats.RefreshUI();
                break;
            case "Inventory":
                world.InventoryWindow.RefreshUI();
                break;
            case "Skills":
                world.SkillWindow.RefreshSkillsEnabled();
                break;
            case "Quests":
                world.Quest.RefreshLog();
                break;
        }
    }

    private void SyncState()
    {
        shown = false;
        foreach (var (key, window) in Windows)
        {
            shown |= window.Visible;
            self.SetWindowOpen(key, window.Visible);
        }

        if (!shown) context.Tooltips.Hide();
    }

    internal void CloseAll()
    {
        foreach (HudWindow window in Windows.Values)
            window.Visible = false;
        SyncState();
    }

    private const string EdgeDockMigration = "edge_dock";

    private const double HudSettleSeconds = 0.5;

    private static readonly string[] LeftDocked = { "Character" };

    private static readonly string[] RightDocked = { "Inventory", "Skills" };

    private readonly List<string> dockOrder = new();
    private bool dockQueued;

    private void BuildDock()
    {
        var docked = LeftDocked.Concat(RightDocked)
            .Where(key => Windows.ContainsKey(key))
            .ToArray();
        Config.ForgetWindowPositionsOnce(EdgeDockMigration, docked.Select(key => Windows[key].Id));

        foreach (string key in docked)
        {
            var window = Windows[key];
            string dockKey = key;
            window.DockTo(() => DockSpot(dockKey));
            window.SetDocked(!Config.HasWindowPos(window.Id));
            window.VisibilityChanged += () => OnDockedVisibility(dockKey, window);
            window.Resized += QueueDockLayout;
            window.Layout.Placed += () =>
            {
                window.SetDocked(false);
                QueueDockLayout();
            };
            if (window.Visible) dockOrder.Add(key);
        }
        context.Root.GetViewport().SizeChanged += QueueDockLayout;
        context.Root.After(HudSettleSeconds, () => QueueDockLayout());
    }

    private void Redock(string key)
    {
        if (!Windows.TryGetValue(key, out var window) || !Config.HasWindowPos(window.Id)) return;
        Config.ForgetWindowPos(window.Id);
        window.SetDocked(true);
        Audio.PlayUi(Sfx.InventoryOpen);
        QueueDockLayout();
    }

    private void OnDockedVisibility(string key, HudWindow window)
    {
        dockOrder.Remove(key);
        if (window.Visible) dockOrder.Add(key);
        QueueDockLayout();
    }

    internal void QueueDockLayout()
    {
        if (dockQueued) return;
        dockQueued = true;
        Callable.From(RunDockLayout).CallDeferred();
    }

    private void RunDockLayout()
    {
        dockQueued = false;
        foreach (string key in dockOrder)
            if (Windows.TryGetValue(key, out var window) && FollowsDock(window))
                window.Layout.ReapplyDefault();
    }

    private static bool FollowsDock(HudWindow window) =>
        GodotObject.IsInstanceValid(window) && window.Visible && !Config.HasWindowPos(window.Id);

    private Vector2 DockSpot(string key)
    {
        bool left = LeftDocked.Contains(key);
        var stack = new List<string>();
        foreach (string k in dockOrder)
        {
            if (LeftDocked.Contains(k) != left) continue;
            if (k == key) break;
            if (Windows.TryGetValue(k, out var earlier) && FollowsDock(earlier)) stack.Add(k);
        }
        stack.Add(key);

        var sizes = stack.Select(k => UiKit.Footprint(Windows[k])).ToArray();
        var frame = new EdgeDock.Frame(
            context.Root.GetViewport().GetVisibleRect().Size,
            Platform.TouchUi ? HudPlacement.TouchEdge : HudAnchor.Edge,
            HudPlacement.BottomInset);
        var spots = EdgeDock.Place(left ? EdgeDock.Side.Left : EdgeDock.Side.Right, sizes, context.Hud.DockRects(), frame);
        return spots[^1];
    }
}
