using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

internal sealed partial class ChatSystem
{
    private const int TabSideMargin = 7;
    private const int OverflowSideMargin = 5;
    private const int TabGap = 3;
    private const float TabBarHeight = 22f;
    private const string OverflowText = "»";
    private const string OverflowUnreadText = "»•";
    private const float TabHoverLighten = 0.3f;
    private const float MenuAlpha = 0.9f;
    private const float FrameEdgeAlpha = 0.25f;
    private const int MenuCorner = 6;
    private const int MenuPadding = 6;
    private static readonly string[] TabColourKeys = { "font_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" };
    private const int CategoryIdBase = 100;
    private const int TimestampsId = 200;
    private const int FontIdBase = 210;
    private const int BackgroundIdBase = 220;
    private const int LockId = 230;
    private const int ColoursId = 240;
    private const int ResetFiltersId = 250;
    private const int ClearChatId = 260;
    private const int NearbyListId = 270;
    private static readonly string[] FontCaptions = { "Small text", "Normal text", "Large text" };
    private static readonly string[] BackgroundCaptions = { "Background 90%", "Background 50%", "Background 20%", "No background" };

    private HBoxContainer _tabBar = null!;
    private Control _tabHost = null!;
    private HBoxContainer _tabStrip = null!;
    private readonly List<ChatTabButton> _tabButtons = new();
    private readonly ButtonGroup _tabGroup = new();
    private Button _overflow = null!;
    private PopupMenu _overflowMenu = null!;
    private Button _moveTool = null!;
    private Button _lockTool = null!;
    private PopupMenu _settingsMenu = null!;
    private int _selectedTab;
    private bool _fitQueued;
    private readonly List<int> _hiddenTabs = new();

    private sealed partial class ChatTabButton : Button
    {
        private const float DotRadius = 2.4f;
        private const float RingRadius = 3.2f;
        private const float DotInset = 5f;
        private bool _unread;

        public bool Unread
        {
            get => _unread;
            set
            {
                if (_unread == value) return;
                _unread = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (!_unread) return;
            var centre = new Vector2(Size.X - DotInset, DotInset);
            DrawCircle(centre, RingRadius, new Color(0, 0, 0, 0.7f));
            DrawCircle(centre, DotRadius, UiTheme.GoldVivid);
        }
    }

    private Control BuildTabBar()
    {
        _tabBar = new HBoxContainer();
        _tabBar.AddThemeConstantOverride("separation", TabGap);

        _tabHost = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipContents = true,
            CustomMinimumSize = new Vector2(0, TabBarHeight),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _tabHost.Resized += QueueFit;
        _tabBar.AddChild(_tabHost);

        _tabStrip = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        _tabStrip.AddThemeConstantOverride("separation", TabGap);
        _tabHost.AddChild(_tabStrip);

        for (int i = 0; i < ChatTabs.All.Count; i++)
        {
            int index = i;
            var tab = new ChatTabButton { Text = ChatTabs.All[i].Caption, ToggleMode = true, ButtonGroup = _tabGroup };
            HudToolButton.Style(tab, TabSideMargin);
            tab.Pressed += () => SelectTab(index, user: true);
            tab.GuiInput += ev =>
            {
                if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
                SelectTab(index, user: true);
                OpenSettingsMenu(tab);
                tab.AcceptEvent();
            };
            _tabButtons.Add(tab);
            _tabStrip.AddChild(tab);
        }

        _overflow = new Button { Text = OverflowText, Visible = false, TooltipText = "More tabs" };
        HudToolButton.Style(_overflow, OverflowSideMargin);
        _overflowMenu = new PopupMenu();
        StyleMenu(_overflowMenu);
        _overflowMenu.IdPressed += id => SelectTab((int)id, user: true);
        _overflow.AddChild(_overflowMenu);
        _overflow.Pressed += OpenOverflowMenu;
        _tabStrip.AddChild(_overflow);

        var background = ToolButton("◐", null, "Background opacity");
        background.Pressed += CycleBackground;
        _moveTool = ToolButton("", "system/move", "Drag to move the chat");
        _lockTool = ToolButton("", "system/unlock", "Lock position and size");
        _lockTool.Pressed += () => SetLocked(!_prefs.Locked);
        var settings = ToolButton("≡", null, "Chat settings");
        settings.Pressed += () => OpenSettingsMenu(settings);

        _settingsMenu = new PopupMenu { HideOnCheckableItemSelection = false };
        StyleMenu(_settingsMenu);
        _settingsMenu.IdPressed += OnSettingsPicked;
        settings.AddChild(_settingsMenu);

        return _tabBar;
    }

    private Button ToolButton(string text, string? icon, string tooltip)
    {
        var button = HudToolButton.Create(text, icon, tooltip);
        _tabBar.AddChild(button);
        return button;
    }

    private void QueueFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Callable.From(FitTabs).CallDeferred();
    }

    private void FitTabs()
    {
        _fitQueued = false;
        if (!GodotObject.IsInstanceValid(_tabHost)) return;
        var widths = new float[_tabButtons.Count];
        for (int i = 0; i < widths.Length; i++) widths[i] = _tabButtons[i].GetCombinedMinimumSize().X;
        float overflowWidth = _overflow.GetCombinedMinimumSize().X;
        var fit = ChatTabs.Fit(widths, _tabHost.Size.X, TabGap, overflowWidth, _selectedTab);

        _hiddenTabs.Clear();
        bool selectedFits = _selectedTab < fit.Visible;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            bool shown = selectedFits ? i < fit.Visible : i < fit.Visible - 1 || i == _selectedTab;
            _tabButtons[i].Visible = shown;
            if (!shown) _hiddenTabs.Add(i);
        }
        _overflow.Visible = _hiddenTabs.Count > 0;
        RefreshOverflow();
        _tabStrip.Size = _tabStrip.GetCombinedMinimumSize();
    }

    private void RefreshOverflow()
    {
        bool unread = false;
        foreach (int i in _hiddenTabs) unread |= _tabButtons[i].Unread;
        _overflow.Text = unread ? OverflowUnreadText : OverflowText;
        _overflow.AddThemeColorOverride("font_color", unread ? UiTheme.GoldBright : new Color("#d4d5d7"));
    }

    private void OpenOverflowMenu()
    {
        _overflowMenu.Clear();
        foreach (int i in _hiddenTabs)
            _overflowMenu.AddItem((_tabButtons[i].Unread ? "• " : "") + ChatTabs.All[i].Caption, i);
        PopupBelow(_overflowMenu, _overflow);
    }

    private static void StyleMenu(PopupMenu menu)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, MenuAlpha),
            BorderColor = new Color(UiTheme.Edge, FrameEdgeAlpha),
        };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(MenuCorner);
        style.SetContentMarginAll(MenuPadding);
        menu.AddThemeStyleboxOverride("panel", style);
    }

    private static void PopupBelow(PopupMenu menu, Control anchor)
    {
        menu.ResetSize();
        var at = anchor.GetScreenPosition();
        menu.Position = (Vector2I)new Vector2(at.X, Mathf.Max(0, at.Y - menu.Size.Y - 4));
        menu.Popup();
    }

    private void SelectTab(int index, bool user)
    {
        if (index < 0 || index >= _tabButtons.Count) return;
        bool changed = index != _selectedTab;
        _selectedTab = index;
        for (int i = 0; i < _tabButtons.Count; i++) _tabButtons[i].SetPressedNoSignal(i == index);
        _tabButtons[index].Unread = false;
        var spec = ChatTabs.All[index];
        if (user && spec.SendChannel != ChatTabs.NoSendChannel
            && (spec.SendChannel != WhisperChannel || _whisperName.Length > 0))
            SetChannel(spec.SendChannel);
        if (!changed) return;
        _prefs.Tab = spec.Id;
        if (user) SavePrefs();
        RebuildLog();
        QueueFit();
    }

    private void MarkUnread(ChatCategory category)
    {
        if (!ChatUnread.Marks(category)) return;
        var filters = new ChatCategory[_tabButtons.Count];
        for (int i = 0; i < filters.Length; i++) filters[i] = _prefs.Filter(ChatTabs.All[i]);
        bool changed = false;
        foreach (int tab in ChatUnread.TabsToMark(filters, category, _selectedTab))
        {
            if (_tabButtons[tab].Unread) continue;
            _tabButtons[tab].Unread = true;
            changed = true;
        }
        if (changed) RefreshOverflow();
    }

    private void OpenSettingsMenu(Control anchor)
    {
        PopulateSettingsMenu();
        PopupBelow(_settingsMenu, anchor);
    }

    private void PopulateSettingsMenu()
    {
        var spec = ChatTabs.All[_selectedTab];
        var filter = _prefs.Filter(spec);
        _settingsMenu.Clear();
        _settingsMenu.AddSeparator($"Show in \"{spec.Caption}\"");
        for (int i = 0; i < ChatCategories.Each.Length; i++)
        {
            var category = ChatCategories.Each[i];
            _settingsMenu.AddCheckItem(ChatCategories.Caption(category), CategoryIdBase + i);
            _settingsMenu.SetItemChecked(_settingsMenu.ItemCount - 1, (filter & category) != 0);
        }
        _settingsMenu.AddSeparator("Look");
        _settingsMenu.AddCheckItem("Timestamps", TimestampsId);
        _settingsMenu.SetItemChecked(_settingsMenu.ItemCount - 1, _prefs.Timestamps);
        for (int i = 0; i < FontCaptions.Length; i++)
        {
            _settingsMenu.AddRadioCheckItem(FontCaptions[i], FontIdBase + i);
            _settingsMenu.SetItemChecked(_settingsMenu.ItemCount - 1, _prefs.FontSize == i);
        }
        for (int i = 0; i < BackgroundCaptions.Length; i++)
        {
            _settingsMenu.AddRadioCheckItem(BackgroundCaptions[i], BackgroundIdBase + i);
            _settingsMenu.SetItemChecked(_settingsMenu.ItemCount - 1, _prefs.Background == i);
        }
        if (!Platform.TouchUi)
        {
            _settingsMenu.AddCheckItem("Show nearby players", NearbyListId);
            _settingsMenu.SetItemChecked(_settingsMenu.ItemCount - 1, _prefs.NearbyShown);
        }
        _settingsMenu.AddCheckItem("Lock position and size", LockId);
        _settingsMenu.SetItemChecked(_settingsMenu.ItemCount - 1, _prefs.Locked);
        _settingsMenu.AddItem("Chat colours…", ColoursId);
        _settingsMenu.AddSeparator();
        _settingsMenu.AddItem("Reset tab filters", ResetFiltersId);
        _settingsMenu.AddItem("Clear chat", ClearChatId);
    }

    private void OnSettingsPicked(long id)
    {
        int pick = (int)id;
        if (pick >= CategoryIdBase && pick < CategoryIdBase + ChatCategories.Each.Length)
        {
            var spec = ChatTabs.All[_selectedTab];
            _prefs.SetFilter(spec, _prefs.Filter(spec) ^ ChatCategories.Each[pick - CategoryIdBase]);
            RebuildLog();
        }
        else if (pick == TimestampsId)
        {
            _prefs.Timestamps = !_prefs.Timestamps;
            RebuildLog();
        }
        else if (pick >= FontIdBase && pick < FontIdBase + FontCaptions.Length)
        {
            _prefs.FontSize = pick - FontIdBase;
            ApplyFontSize();
        }
        else if (pick >= BackgroundIdBase && pick < BackgroundIdBase + BackgroundCaptions.Length)
        {
            _prefs.Background = pick - BackgroundIdBase;
            ApplyBackground();
        }
        else if (pick == NearbyListId)
        {
            SetNearbyShown(!_prefs.NearbyShown);
            PopulateSettingsMenu();
            return;
        }
        else if (pick == LockId)
        {
            SetLocked(!_prefs.Locked);
            return;
        }
        else if (pick == ColoursId)
        {
            _settingsMenu.Hide();
            ColorsRequested?.Invoke();
            return;
        }
        else if (pick == ResetFiltersId)
        {
            _prefs.ResetFilters();
            RebuildLog();
        }
        else if (pick == ClearChatId)
        {
            ClearLog();
            return;
        }
        SavePrefs();
        PopulateSettingsMenu();
    }

    private void TintTabs()
    {
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            Color colour = _colors.ForTab(ChatTabs.All[i]);
            foreach (string key in TabColourKeys) _tabButtons[i].AddThemeColorOverride(key, colour);
            _tabButtons[i].AddThemeColorOverride("font_hover_color", colour.Lightened(TabHoverLighten));
        }
    }

    private void ApplyLook()
    {
        TintTabs();
        ApplyBackground();
        ApplyFontSize();
        ApplyLock();
        int tab = ChatTabs.IndexOf(_prefs.Tab);
        _selectedTab = tab < 0 ? 0 : tab;
        _tabButtons[_selectedTab].SetPressedNoSignal(true);
        UpdateInputColour();
        QueueFit();
    }

    private void CycleBackground()
    {
        _prefs.Background = (_prefs.Background + 1) % ChatPrefs.Backgrounds.Length;
        ApplyBackground();
        SavePrefs();
    }

    private void ApplyBackground()
    {
        float alpha = ChatPrefs.Backgrounds[_prefs.Background];
        _panelStyle.BgColor = new Color(0, 0, 0, alpha);
        _panelStyle.BorderColor = new Color(UiTheme.Edge, alpha > 0 ? FrameEdgeAlpha : 0f);
        _nearby?.SetBackground(alpha);
    }

    private void ApplyFontSize()
    {
        int size = ChatPrefs.FontSizes[_prefs.FontSize];
        foreach (var key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" })
            _scroll.AddThemeFontSizeOverride(key, size);
    }

    private void SetLocked(bool locked)
    {
        _prefs.Locked = locked;
        ApplyLock();
        SavePrefs();
        if (_settingsMenu.Visible) PopulateSettingsMenu();
    }

    private void ApplyLock()
    {
        if (_layout != null) _layout.Locked = _prefs.Locked;
        _moveTool.Visible = !Platform.TouchUi && !_prefs.Locked;
        HudToolButton.ShowLocked(_lockTool, _prefs.Locked);
    }

    private void SavePrefs() => Config.SetChatLook(_prefs.Format());

    internal void PreviewLook(string tab, bool timestamps, int font, int background, bool locked)
    {
        _prefs.Timestamps = timestamps;
        _prefs.FontSize = Mathf.Clamp(font, 0, ChatPrefs.FontSizes.Length - 1);
        _prefs.Background = Mathf.Clamp(background, 0, ChatPrefs.Backgrounds.Length - 1);
        _prefs.Locked = locked;
        ApplyBackground();
        ApplyFontSize();
        ApplyLock();
        int index = ChatTabs.IndexOf(tab);
        if (index >= 0 && index != _selectedTab) SelectTab(index, user: false);
        else RebuildLog();
    }

    internal void PreviewMenu(string which)
    {
        if (which == "settings" && _settingsMenu.GetParent() is Control settings) OpenSettingsMenu(settings);
        else if (which == "overflow" && _overflow.Visible) OpenOverflowMenu();
    }

    internal void ApplyColors(ChatColors colors)
    {
        _colors = colors;
        Config.SetChatColors(colors.Format());
        TintTabs();
        RebuildLog();
        UpdateInputColour();
    }
}
