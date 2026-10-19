using Godot;

namespace LibreKO.Features.Social;

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

    private HBoxContainer tabBar = null!;
    private Control tabHost = null!;
    private HBoxContainer tabStrip = null!;
    private readonly List<ChatTabButton> tabButtons = new();
    private readonly ButtonGroup tabGroup = new();
    private Button overflow = null!;
    private PopupMenu overflowMenu = null!;
    private Button moveTool = null!;
    private Button lockTool = null!;
    private PopupMenu settingsMenu = null!;
    private int selectedTab;
    private bool fitQueued;
    private readonly List<int> hiddenTabs = new();

    private sealed partial class ChatTabButton : Button
    {
        private const float DotRadius = 2.4f;
        private const float RingRadius = 3.2f;
        private const float DotInset = 5f;
        private bool unread;

        public bool Unread
        {
            get => unread;
            set
            {
                if (unread == value) return;
                unread = value;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (!unread) return;
            var centre = new Vector2(Size.X - DotInset, DotInset);
            DrawCircle(centre, RingRadius, new Color(0, 0, 0, 0.7f));
            DrawCircle(centre, DotRadius, UiTheme.GoldVivid);
        }
    }

    private Control BuildTabBar()
    {
        tabBar = new HBoxContainer();
        tabBar.AddThemeConstantOverride("separation", TabGap);

        tabHost = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            ClipContents = true,
            CustomMinimumSize = new Vector2(0, TabBarHeight),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        tabHost.Resized += QueueFit;
        tabBar.AddChild(tabHost);

        tabStrip = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        tabStrip.AddThemeConstantOverride("separation", TabGap);
        tabHost.AddChild(tabStrip);

        for (int i = 0; i < ChatTabs.All.Count; i++)
        {
            int index = i;
            var tab = new ChatTabButton { Text = ChatTabs.All[i].Caption, ToggleMode = true, ButtonGroup = tabGroup };
            HudToolButton.Style(tab, TabSideMargin);
            tab.Pressed += () => SelectTab(index, user: true);
            tab.GuiInput += ev =>
            {
                if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;
                SelectTab(index, user: true);
                OpenSettingsMenu(tab);
                tab.AcceptEvent();
            };
            tabButtons.Add(tab);
            tabStrip.AddChild(tab);
        }

        overflow = new Button { Text = OverflowText, Visible = false, TooltipText = "More tabs" };
        HudToolButton.Style(overflow, OverflowSideMargin);
        overflowMenu = new PopupMenu();
        StyleMenu(overflowMenu);
        overflowMenu.IdPressed += id => SelectTab((int)id, user: true);
        overflow.AddChild(overflowMenu);
        overflow.Pressed += OpenOverflowMenu;
        tabStrip.AddChild(overflow);

        var background = ToolButton("◐", null, "Background opacity");
        background.Pressed += CycleBackground;
        moveTool = ToolButton("", "system/move", "Drag to move the chat");
        lockTool = ToolButton("", "system/unlock", "Lock position and size");
        lockTool.Pressed += () => SetLocked(!prefs.Locked);
        var settings = ToolButton("≡", null, "Chat settings");
        settings.Pressed += () => OpenSettingsMenu(settings);

        settingsMenu = new PopupMenu { HideOnCheckableItemSelection = false };
        StyleMenu(settingsMenu);
        settingsMenu.IdPressed += OnSettingsPicked;
        settings.AddChild(settingsMenu);

        return tabBar;
    }

    private Button ToolButton(string text, string? icon, string tooltip)
    {
        var button = HudToolButton.Create(text, icon, tooltip);
        tabBar.AddChild(button);
        return button;
    }

    private const string GeneralTabId = "general";

    internal void NoahStatusChanged() => QueueFit();

    private void QueueFit()
    {
        if (fitQueued) return;
        fitQueued = true;
        Callable.From(FitTabs).CallDeferred();
    }

    private void FitTabs()
    {
        fitQueued = false;
        if (!GodotObject.IsInstanceValid(tabHost)) return;
        bool noah = host?.NoahKnight == true;
        var usable = new List<int>();
        for (int i = 0; i < tabButtons.Count; i++)
        {
            if (ChatTabs.Applies(ChatTabs.All[i], noah)) usable.Add(i);
            else tabButtons[i].Visible = false;
        }
        if (!usable.Contains(selectedTab))
        {
            Callable.From(() => SelectTab(ChatTabs.IndexOf(GeneralTabId), false)).CallDeferred();
            return;
        }
        var widths = new float[usable.Count];
        for (int i = 0; i < widths.Length; i++) widths[i] = tabButtons[usable[i]].GetCombinedMinimumSize().X;
        float overflowWidth = overflow.GetCombinedMinimumSize().X;
        int selectedSlot = usable.IndexOf(selectedTab);
        var fit = ChatTabs.Fit(widths, tabHost.Size.X, TabGap, overflowWidth, selectedSlot);

        hiddenTabs.Clear();
        bool selectedFits = selectedSlot < fit.Visible;
        for (int slot = 0; slot < usable.Count; slot++)
        {
            int i = usable[slot];
            bool shown = selectedFits ? slot < fit.Visible : slot < fit.Visible - 1 || i == selectedTab;
            tabButtons[i].Visible = shown;
            if (!shown) hiddenTabs.Add(i);
        }
        overflow.Visible = hiddenTabs.Count > 0;
        RefreshOverflow();
        tabStrip.Size = tabStrip.GetCombinedMinimumSize();
    }

    private void RefreshOverflow()
    {
        bool unread = false;
        foreach (int i in hiddenTabs) unread |= tabButtons[i].Unread;
        overflow.Text = unread ? OverflowUnreadText : OverflowText;
        overflow.AddThemeColorOverride("font_color", unread ? UiTheme.GoldBright : new Color("#d4d5d7"));
    }

    private void OpenOverflowMenu()
    {
        overflowMenu.Clear();
        foreach (int i in hiddenTabs)
            overflowMenu.AddItem((tabButtons[i].Unread ? "• " : "") + ChatTabs.All[i].Caption, i);
        PopupBelow(overflowMenu, overflow);
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
        if (index < 0 || index >= tabButtons.Count) return;
        bool changed = index != selectedTab;
        selectedTab = index;
        for (int i = 0; i < tabButtons.Count; i++) tabButtons[i].SetPressedNoSignal(i == index);
        tabButtons[index].Unread = false;
        var spec = ChatTabs.All[index];
        if (user && spec.SendChannel != ChatTabs.NoSendChannel
            && (spec.SendChannel != WhisperChannel || whisperName.Length > 0))
            SetChannel(spec.SendChannel);
        if (!changed) return;
        prefs.Tab = spec.Id;
        if (user) SavePrefs();
        RebuildLog();
        QueueFit();
    }

    private void MarkUnread(ChatCategory category)
    {
        if (!ChatUnread.Marks(category)) return;
        var filters = new ChatCategory[tabButtons.Count];
        for (int i = 0; i < filters.Length; i++) filters[i] = prefs.Filter(ChatTabs.All[i]);
        bool changed = false;
        foreach (int tab in ChatUnread.TabsToMark(filters, category, selectedTab))
        {
            if (tabButtons[tab].Unread) continue;
            tabButtons[tab].Unread = true;
            changed = true;
        }
        if (changed) RefreshOverflow();
    }

    private void OpenSettingsMenu(Control anchor)
    {
        PopulateSettingsMenu();
        PopupBelow(settingsMenu, anchor);
    }

    private void PopulateSettingsMenu()
    {
        var spec = ChatTabs.All[selectedTab];
        var filter = prefs.Filter(spec);
        settingsMenu.Clear();
        settingsMenu.AddSeparator($"Show in \"{spec.Caption}\"");
        for (int i = 0; i < ChatCategories.Each.Length; i++)
        {
            var category = ChatCategories.Each[i];
            settingsMenu.AddCheckItem(ChatCategories.Caption(category), CategoryIdBase + i);
            settingsMenu.SetItemChecked(settingsMenu.ItemCount - 1, (filter & category) != 0);
        }
        settingsMenu.AddSeparator("Look");
        settingsMenu.AddCheckItem("Timestamps", TimestampsId);
        settingsMenu.SetItemChecked(settingsMenu.ItemCount - 1, prefs.Timestamps);
        for (int i = 0; i < FontCaptions.Length; i++)
        {
            settingsMenu.AddRadioCheckItem(FontCaptions[i], FontIdBase + i);
            settingsMenu.SetItemChecked(settingsMenu.ItemCount - 1, prefs.FontSize == i);
        }
        for (int i = 0; i < BackgroundCaptions.Length; i++)
        {
            settingsMenu.AddRadioCheckItem(BackgroundCaptions[i], BackgroundIdBase + i);
            settingsMenu.SetItemChecked(settingsMenu.ItemCount - 1, prefs.Background == i);
        }
        if (!Platform.TouchUi)
        {
            settingsMenu.AddCheckItem("Show nearby players", NearbyListId);
            settingsMenu.SetItemChecked(settingsMenu.ItemCount - 1, prefs.NearbyShown);
        }
        settingsMenu.AddCheckItem("Lock position and size", LockId);
        settingsMenu.SetItemChecked(settingsMenu.ItemCount - 1, prefs.Locked);
        settingsMenu.AddItem("Chat colours…", ColoursId);
        settingsMenu.AddSeparator();
        settingsMenu.AddItem("Reset tab filters", ResetFiltersId);
        settingsMenu.AddItem("Clear chat", ClearChatId);
    }

    private void OnSettingsPicked(long id)
    {
        int pick = (int)id;
        if (pick >= CategoryIdBase && pick < CategoryIdBase + ChatCategories.Each.Length)
        {
            var spec = ChatTabs.All[selectedTab];
            prefs.SetFilter(spec, prefs.Filter(spec) ^ ChatCategories.Each[pick - CategoryIdBase]);
            RebuildLog();
        }
        else if (pick == TimestampsId)
        {
            prefs.Timestamps = !prefs.Timestamps;
            RebuildLog();
        }
        else if (pick >= FontIdBase && pick < FontIdBase + FontCaptions.Length)
        {
            prefs.FontSize = pick - FontIdBase;
            ApplyFontSize();
        }
        else if (pick >= BackgroundIdBase && pick < BackgroundIdBase + BackgroundCaptions.Length)
        {
            prefs.Background = pick - BackgroundIdBase;
            ApplyBackground();
        }
        else if (pick == NearbyListId)
        {
            SetNearbyShown(!prefs.NearbyShown);
            PopulateSettingsMenu();
            return;
        }
        else if (pick == LockId)
        {
            SetLocked(!prefs.Locked);
            return;
        }
        else if (pick == ColoursId)
        {
            settingsMenu.Hide();
            ColorsRequested?.Invoke();
            return;
        }
        else if (pick == ResetFiltersId)
        {
            prefs.ResetFilters();
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
        for (int i = 0; i < tabButtons.Count; i++)
        {
            Color colour = colors.ForTab(ChatTabs.All[i]);
            foreach (string key in TabColourKeys) tabButtons[i].AddThemeColorOverride(key, colour);
            tabButtons[i].AddThemeColorOverride("font_hover_color", colour.Lightened(TabHoverLighten));
        }
    }

    private void ApplyLook()
    {
        TintTabs();
        ApplyBackground();
        ApplyFontSize();
        ApplyLock();
        int tab = ChatTabs.IndexOf(prefs.Tab);
        selectedTab = tab < 0 ? 0 : tab;
        tabButtons[selectedTab].SetPressedNoSignal(true);
        UpdateInputColour();
        QueueFit();
    }

    private void CycleBackground()
    {
        prefs.Background = (prefs.Background + 1) % ChatPrefs.Backgrounds.Length;
        ApplyBackground();
        SavePrefs();
    }

    private void ApplyBackground()
    {
        float alpha = ChatPrefs.Backgrounds[prefs.Background];
        panelStyle.BgColor = new Color(0, 0, 0, alpha);
        panelStyle.BorderColor = new Color(UiTheme.Edge, alpha > 0 ? FrameEdgeAlpha : 0f);
        nearby?.SetBackground(alpha);
    }

    private void ApplyFontSize()
    {
        int size = ChatPrefs.FontSizes[prefs.FontSize];
        foreach (var key in new[] { "normal_font_size", "bold_font_size", "italics_font_size", "bold_italics_font_size" })
            scroll.AddThemeFontSizeOverride(key, size);
    }

    private void SetLocked(bool locked)
    {
        prefs.Locked = locked;
        ApplyLock();
        SavePrefs();
        if (settingsMenu.Visible) PopulateSettingsMenu();
    }

    private void ApplyLock()
    {
        if (layout != null) layout.Locked = prefs.Locked;
        moveTool.Visible = !Platform.TouchUi && !prefs.Locked;
        HudToolButton.ShowLocked(lockTool, prefs.Locked);
    }

    private void SavePrefs() => Config.SetChatLook(prefs.Format());

    internal void PreviewLook(string tab, bool timestamps, int font, int background, bool locked)
    {
        prefs.Timestamps = timestamps;
        prefs.FontSize = Mathf.Clamp(font, 0, ChatPrefs.FontSizes.Length - 1);
        prefs.Background = Mathf.Clamp(background, 0, ChatPrefs.Backgrounds.Length - 1);
        prefs.Locked = locked;
        ApplyBackground();
        ApplyFontSize();
        ApplyLock();
        int index = ChatTabs.IndexOf(tab);
        if (index >= 0 && index != selectedTab) SelectTab(index, user: false);
        else RebuildLog();
    }

    internal void PreviewMenu(string which)
    {
        if (which == "settings" && settingsMenu.GetParent() is Control settings) OpenSettingsMenu(settings);
        else if (which == "overflow" && overflow.Visible) OpenOverflowMenu();
    }

    internal void ApplyColors(ChatColors colors)
    {
        this.colors = colors;
        Config.SetChatColors(colors.Format());
        TintTabs();
        RebuildLog();
        UpdateInputColour();
    }
}
