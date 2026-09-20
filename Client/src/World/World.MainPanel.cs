using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _mainLayer = null!;
    private readonly Dictionary<string, HudWindow> _mainWindows = new();
    private bool _mainShown;

    internal enum CharacterPage { Character, Friends }

    private const int CharacterPageWidth = 268;

    private readonly Dictionary<CharacterPage, Button> _characterPageTabs = new();
    private readonly Dictionary<CharacterPage, Control> _characterPages = new();

    private Control BuildCharacterPages()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 8);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 2);
        root.AddChild(tabs);

        _characterPages[CharacterPage.Character] = _statsContent;
        _characterPages[CharacterPage.Friends] = _friendsContent;

        foreach (var (page, content) in _characterPages)
        {
            var which = page;
            var button = UiTheme.TopTabButton(page == CharacterPage.Character ? "Character" : "Friends", 12);
            button.Pressed += () => ShowCharacterPage(which);
            _characterPageTabs[page] = button;
            tabs.AddChild(button);
            root.AddChild(content);
        }

        ShowCharacterPage(CharacterPage.Character);
        return root;
    }

    private void ShowCharacterPage(CharacterPage page)
    {
        foreach (var (key, content) in _characterPages) content.Visible = key == page;
        foreach (var (key, button) in _characterPageTabs) button.ButtonPressed = key == page;
        if (page == CharacterPage.Friends) EnsureFriendsLoaded();
        if (_mainWindows.TryGetValue("Character", out HudWindow? window))
            Callable.From(window.ResetSize).CallDeferred();
    }

    private void OpenCharacterPage(CharacterPage page)
    {
        if (!_mainWindows.TryGetValue("Character", out HudWindow? window)) return;
        if (!window.Visible || _characterPageTabs[page].ButtonPressed == false)
        {
            if (!window.Visible) ToggleMainWindow("Character");
            ShowCharacterPage(page);
            return;
        }
        ToggleMainWindow("Character");
    }

    private bool CharTabOpen()   => MainWindowOpen("Inventory");
    private bool SkillsTabOpen() => MainWindowOpen("Skills");
    private bool QuestsTabOpen() => MainWindowOpen("Quests");
    private bool PartyTabOpen()  => MainWindowOpen("Party");

    private void MainPanelInit()
    {
        _mainLayer = new CanvasLayer { Layer = 72 };
        AddChild(_mainLayer);

        AddMainWindow(
            "Character", "character_info", "Character Info", new Vector2(64, 82),
            BuildCharacterPages(), CharacterPageWidth, UiIcons.Get("game/helmet"));
        AddMainWindow(
            "Inventory", "inventory", "INVENTORY", new Vector2(334, 66),
            _invContent, 0, UiIcons.Get("system/bag")).SetBackgroundAlpha(InventoryWindowAlpha);
        AddMainWindow(
            "Skills", "skills", "Skills", new Vector2(120, 84),
            _skillsContent, 330, UiIcons.Get("game/main-hand"));
        AddMainWindow(
            "Quests", "quests", "QUESTS", new Vector2(170, 86),
            _questsContent, 420, UiIcons.Get("system/scroll"));
        AddMainWindow(
            "Party", "party", "Party", new Vector2(1060, 82),
            _partyContent, 252, UiIcons.Get("system/users-three"));

        RestoreMainWindows();
    }

    private void RestoreMainWindows()
    {
        foreach (var (key, window) in _mainWindows)
        {
            if (!Net.I.IsWindowOpen(key)) continue;
            window.Visible = true;
            RefreshMainWindow(key);
        }
        SyncMainWindowState();
    }

    private const float InventoryWindowAlpha = 0.86f;

    private HudWindow AddMainWindow(
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
        window.Closed += SyncMainWindowState;
        window.Body.AddChild(content);
        _mainLayer.AddChild(window);
        Callable.From(window.ResetSize).CallDeferred();
        _mainWindows[key] = window;
        return window;
    }

    public bool MainWindowOpen(string key) =>
        _mainWindows.TryGetValue(key, out HudWindow? window) && window.Visible;

    private void ToggleMainWindow(string key)
    {
        if (!_mainWindows.TryGetValue(key, out HudWindow? window)) return;
        if (window.Visible)
        {
            window.Visible = false;
            if (key == "Inventory") { HideItemTooltip(); HideDeletePrompt(); }
            Audio.PlayUi(Sfx.InventoryClose);
        }
        else
        {
            window.Visible = true;
            window.GetParent()?.MoveChild(window, window.GetParent().GetChildCount() - 1);
            RefreshMainWindow(key);
            Audio.PlayUi(Sfx.InventoryOpen);
        }
        SyncMainWindowState();
    }

    private void ShowMainWindow(string key)
    {
        if (!_mainWindows.TryGetValue(key, out HudWindow? window)) return;
        window.Visible = true;
        window.GetParent()?.MoveChild(window, window.GetParent().GetChildCount() - 1);
        RefreshMainWindow(key);
        SyncMainWindowState();
    }

    private void RefreshMainWindow(string key)
    {
        switch (key)
        {
            case "Character":
                RefreshStatsUI();
                break;
            case "Inventory":
                RefreshInventoryUI();
                break;
            case "Skills":
                RefreshSkillsEnabled();
                break;
            case "Quests":
                Net.I.SendQuestLogRequest();
                RefreshQuestLog();
                break;
            case "Party":
                RefreshPartyUI();
                break;
        }
    }

    private void SyncMainWindowState()
    {
        _mainShown = false;
        foreach (var (key, window) in _mainWindows)
        {
            _mainShown |= window.Visible;
            Net.I.SetWindowOpen(key, window.Visible);
        }

        if (_mainShown)
        {
            return;
        }

        HideItemTooltip();
    }

    private void SetMainShown(bool show)
    {
        if (show)
        {
            ShowMainWindow("Character");
            return;
        }

        foreach (HudWindow window in _mainWindows.Values)
            window.Visible = false;
        SyncMainWindowState();
    }

    private static Button MakeSubTabButton<TKey>(
        string label,
        TKey key,
        System.Action onPress,
        Dictionary<TKey, Button> registry)
        where TKey : notnull
    {
        var button = new Button
        {
            Text = label,
            ToggleMode = true,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", 12);
        button.Pressed += () => onPress();
        registry[key] = button;
        return button;
    }
}
