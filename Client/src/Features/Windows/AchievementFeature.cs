using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AchievementFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly AchievementSession achievement;
    private readonly IAchievementRequests requests;
    private readonly AchievementTrophyFeature trophy;
    private readonly TitlePickerFeature titlePicker;

    public AchievementFeature(IWorldContext context, AchievementSession achievement, IAchievementRequests requests, AchievementTrophyFeature trophy, TitlePickerFeature titlePicker)
    {
        this.context = context;
        this.achievement = achievement;
        this.requests = requests;
        this.trophy = trophy;
        this.titlePicker = titlePicker;
    }

    private const int AchListWidth = 520;
    private const int AchListHeight = 360;
    private const int AchScoreCardWidth = 118;
    private const int AchProgressLabelWidth = 74;
    private const int AchProgressCountWidth = 62;
    private const int AchRecentTextWidth = 210;

    private CanvasLayer achLayer = null!;
    internal HudWindow AchPanel { get; private set; } = null!;
    private ScrollContainer achScroll = null!;
    private VBoxContainer achList = null!;
    private HBoxContainer achTabs = null!;
    private Label achSummary = null!;
    private Label achTitleBanner = null!;
    private const float AchRewardSlotSize = 36f;
    private const float AchRowBarHeight = 4f;
    private const int AchCategoryWidth = 136;
    private HBoxContainer achListRow = null!;
    private VBoxContainer achCategoryBox = null!;
    internal int Group { get; set; } = AchievementData.AllGroups;
    internal CheckButton AchHideClaimed { get; private set; } = null!;
    private LineEdit achSearch = null!;
    internal OptionButton AchStatus { get; private set; } = null!;
    internal AchievementStatusFilter StatusFilter { get; set; } = AchievementStatusFilter.All;
    internal bool Shown { get; set; }

    internal bool ViewingSummary { get; set; } = true;
    private VBoxContainer achSummaryPage = null!;
    private Button achSummaryTabButton = null!;
    internal AchievementTab Tab { get; set; } = AchievementTab.War;
    private readonly List<AchievementEntry> achVisible = new();
    private readonly Dictionary<AchievementTab, Button> achTabButtons = new();

    private readonly Dictionary<AchievementTab, int> achClaimablePerTab = new();

    public void Build()
    {
        achLayer = new CanvasLayer { Layer = UiLayers.Boards };
        context.Root.AddChild(achLayer);
        AchPanel = new HudWindow("achievements", "Achievements", new Vector2(160, 110),
            bodyMinWidth: AchListWidth)
        { Visible = false };
        AchPanel.Closed += CloseAchievements;
        achLayer.AddChild(AchPanel);

        var root = AchPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        achTabs = new HBoxContainer();
        achTabs.AddThemeConstantOverride("separation", 4);
        root.AddChild(achTabs);

        achSummaryTabButton = UiTheme.TopTabButton("Summary");
        achSummaryTabButton.Pressed += ShowSummaryPage;
        achTabs.AddChild(achSummaryTabButton);

        foreach (var category in AchievementData.Tabs)
        {
            var value = category;
            var btn = UiTheme.TopTabButton(AchievementData.TabName(category));
            btn.Pressed += () => SelectTab(value);
            achTabButtons[category] = btn;
            achTabs.AddChild(btn);
        }

        var banner = new PanelContainer();
        banner.AddThemeStyleboxOverride("panel", UiTheme.HeaderBand(2));
        root.AddChild(banner);
        var bannerRow = new HBoxContainer();
        bannerRow.AddThemeConstantOverride("separation", 8);
        banner.AddChild(bannerRow);
        achTitleBanner = UiTheme.Text("", 13, UiTheme.GoldBright);
        achTitleBanner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        achTitleBanner.VerticalAlignment = VerticalAlignment.Center;
        bannerRow.AddChild(achTitleBanner);
        var changeTitle = new Button { Text = "Change title", FocusMode = Control.FocusModeEnum.None };
        changeTitle.AddThemeFontSizeOverride("font_size", 12);
        changeTitle.Pressed += titlePicker.Toggle;
        bannerRow.AddChild(changeTitle);

        var summaryRow = new HBoxContainer();
        summaryRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(summaryRow);

        achSummary = HudStyle.Label(13);
        achSummary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        summaryRow.AddChild(achSummary);

        AchHideClaimed = new CheckButton { Text = "Hide claimed", FocusMode = Control.FocusModeEnum.None };
        AchHideClaimed.AddThemeFontSizeOverride("font_size", 12);
        AchHideClaimed.Toggled += _ => RebuildList(keepScroll: false);
        summaryRow.AddChild(AchHideClaimed);

        var filterRow = new HBoxContainer();
        filterRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(filterRow);
        achSearch = new LineEdit
        {
            PlaceholderText = "Search achievements",
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        achSearch.AddThemeFontSizeOverride("font_size", 12);
        achSearch.TextChanged += _ => RebuildList(keepScroll: false);
        filterRow.AddChild(achSearch);
        AchStatus = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        AchStatus.AddThemeFontSizeOverride("font_size", 12);
        foreach (var (_, label) in AchievementFilter.Options) AchStatus.AddItem(label);
        AchStatus.ItemSelected += index =>
        {
            StatusFilter = AchievementFilter.Options[index].Filter;
            RebuildList(keepScroll: false);
        };
        filterRow.AddChild(AchStatus);

        achScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(AchListWidth, AchListHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        achListRow = new HBoxContainer();
        achListRow.AddThemeConstantOverride("separation", 6);
        root.AddChild(achListRow);
        var categoryPanel = UiTheme.Section();
        categoryPanel.CustomMinimumSize = new Vector2(AchCategoryWidth, 0);
        achListRow.AddChild(categoryPanel);
        achCategoryBox = new VBoxContainer();
        achCategoryBox.AddThemeConstantOverride("separation", 2);
        categoryPanel.AddChild(achCategoryBox);
        achListRow.AddChild(achScroll);
        achList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        achList.AddThemeConstantOverride("separation", 3);
        achScroll.AddChild(achList);

        achSummaryPage = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(AchListWidth, AchListHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        achSummaryPage.AddThemeConstantOverride("separation", 8);
        root.AddChild(achSummaryPage);

        trophy.Pressed += ToggleAchievements;
        achievement.AchievementListEvent += OnList;
        achievement.AchievementSummaryEvent += OnSummary;
        achievement.AchievementClaimEvent += OnClaim;
        achievement.TitleChangedEvent += OnTitleChanged;
        ApplyPageVisibility();
        RefreshCounts();
        RebuildList(keepScroll: false);
        RebuildSummary();
        requests.List();
        requests.Summary();
    }

    bool IEscapable.CloseOnEscape()
    {
        if (Shown) CloseAchievements();
        else return false;
        return true;
    }

    public void Dispose()
    {
        trophy.Pressed -= ToggleAchievements;
        achievement.AchievementListEvent -= OnList;
        achievement.AchievementSummaryEvent -= OnSummary;
        achievement.AchievementClaimEvent -= OnClaim;
        achievement.TitleChangedEvent -= OnTitleChanged;
    }

    private void OnTitleChanged(int charId, int titleId)
    {
        if (charId != context.Self.CharId) return;
        RefreshTitleBanner();
        if (Shown) RebuildList(keepScroll: true);
    }

    internal void ToggleAchievements()
    {
        if (Shown) { CloseAchievements(); return; }
        AchPanel.Visible = true;
        Shown = true;
        RebuildList(keepScroll: false);
        requests.List();
        requests.Summary();
    }

    internal void CloseAchievements()
    {
        if (!Shown) return;
        Shown = false;
        AchPanel.Visible = false;
    }

    private void SelectTab(AchievementTab category)
    {
        if (Tab == category && !ViewingSummary) return;
        Tab = category;
        Group = AchievementData.AllGroups;
        ViewingSummary = false;
        ApplyPageVisibility();
        RefreshCounts();
        RebuildList(keepScroll: false);
    }

    private void SelectGroup(int group)
    {
        if (Group == group) return;
        Group = group;
        RebuildList(keepScroll: false);
    }

    private void RebuildCategories()
    {
        foreach (var child in achCategoryBox.GetChildren()) child.QueueFree();
        achCategoryBox.AddChild(BuildCategoryRow("All", AchievementData.AllGroups));
        foreach (int group in AchievementData.CategoriesOf(Tab))
            achCategoryBox.AddChild(BuildCategoryRow(AchievementData.CategoryName(group), group));
    }

    private Control BuildCategoryRow(string name, int group)
    {
        bool chosen = Group == group;
        var row = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        var box = UiTheme.Row(selected: true);
        if (!chosen) { box.BgColor = Colors.Transparent; box.BorderColor = Colors.Transparent; }
        row.AddThemeStyleboxOverride("panel", box);
        var label = UiTheme.Text(name, 12, chosen ? UiTheme.GoldBright : UiTheme.TextLo);
        label.ClipText = true;
        row.AddChild(label);
        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                SelectGroup(group);
        };
        return row;
    }

    private void ShowSummaryPage()
    {
        if (ViewingSummary) return;
        ViewingSummary = true;
        ApplyPageVisibility();
        RefreshCounts();
        RebuildSummary();
    }

    internal void ApplyPageVisibility()
    {
        achListRow.Visible = !ViewingSummary;
        achSummaryPage.Visible = ViewingSummary;
        AchHideClaimed.Visible = !ViewingSummary;
        achSearch.GetParent<Control>().Visible = !ViewingSummary;
        achSummary.Visible = !ViewingSummary;
    }

    internal void OnSummary(AchievementSummary summary)
    {
        if (Shown) RebuildSummary();
    }

    internal void OnList(List<AchievementEntry> list)
    {
        RefreshCounts();
        if (Shown) RebuildList(keepScroll: true);
    }

    internal void RefreshCounts()
    {
        achClaimablePerTab.Clear();
        int claimed = 0, points = 0;
        int inCategory = 0, claimedInCategory = 0;

        foreach (var entry in achievement.Entries)
        {
            if (AchievementData.Get(entry.Id) is not { } info) continue;
            bool earned = entry.Claimed || entry.Claimable;
            if (earned) { claimed++; points += info.Points; }
            if (entry.Claimable)
                achClaimablePerTab[info.Tab] =
                    achClaimablePerTab.GetValueOrDefault(info.Tab) + 1;
            if (info.Tab != Tab) continue;
            inCategory++;
            if (earned) claimedInCategory++;
        }

        RefreshTabs();
        RefreshTitleBanner();

        achSummary.Text = achievement.Entries.Count == 0
            ? "Waiting for the server…"
            : $"{AchievementData.TabName(Tab)} {claimedInCategory} / {inCategory}"
              + $"   ·   {claimed} of {achievement.Entries.Count} earned   ·   {points} points";
    }

    internal void RefreshTitleBanner()
    {
        if (!GodotObject.IsInstanceValid(achTitleBanner)) return;
        string worn = AchievementData.TitleOf(achievement.DisplayTitleId) is { } title ? title.Name : "None";
        achTitleBanner.Text = $"Title: {worn}";
    }

    internal void RebuildSummary()
    {
        foreach (var child in achSummaryPage.GetChildren())
            child.QueueFree();

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        achSummaryPage.AddChild(top);
        top.AddChild(BuildScoreCard());
        top.AddChild(BuildTabProgress());

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        bottom.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        achSummaryPage.AddChild(bottom);
        bottom.AddChild(BuildReport());
        bottom.AddChild(BuildRecent());
    }

    private Control BuildScoreCard()
    {
        var section = UiTheme.Section();
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(AchScoreCardWidth, 0) };
        col.AddThemeConstantOverride("separation", 2);
        section.AddChild(col);

        col.AddChild(UiTheme.Text("Achievement Points", 11, UiTheme.TextLo, HorizontalAlignment.Center));
        col.AddChild(UiTheme.Heading(26, achievement.Summary is { } report ? $"{report.Points:N0}" : "-"));
        return section;
    }

    private Control BuildTabProgress()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 3);
        section.AddChild(col);

        int doneTotal = 0, ofTotal = 0;
        var rows = new List<(string Name, int Done, int Of)>();
        foreach (var tab in AchievementData.Tabs)
        {
            int done = 0, of = 0;
            foreach (var entry in achievement.Entries)
            {
                if (AchievementData.Get(entry.Id) is not { } info || info.Tab != tab) continue;
                of++;
                if (entry.Claimed || entry.Claimable) done++;
            }

            doneTotal += done;
            ofTotal += of;
            rows.Add((AchievementData.TabName(tab), done, of));
        }

        col.AddChild(BuildProgressRow("Total", doneTotal, ofTotal, UiTheme.GoldBright, bold: true));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        for (int i = 0; i < rows.Count; i++)
        {
            var row = BuildProgressRow(rows[i].Name, rows[i].Done, rows[i].Of, UiTheme.Bronze);
            var tab = AchievementData.Tabs[i];
            row.MouseFilter = Control.MouseFilterEnum.Stop;
            row.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
            row.TooltipText = $"Open {rows[i].Name}";
            row.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                    SelectTab(tab);
            };
            col.AddChild(row);
        }

        return section;
    }

    private static Control BuildProgressRow(
        string name, int done, int of, Color fill, bool bold = false)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);

        var label = UiTheme.Text(name, 12, bold ? UiTheme.GoldBright : UiTheme.TextHi);
        if (bold) label.AddThemeConstantOverride("font_embolden", 1);
        label.CustomMinimumSize = new Vector2(AchProgressLabelWidth, 0);
        row.AddChild(label);

        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = Math.Max(1, of),
            Value = done,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 12),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        var track = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.45f) };
        track.SetCornerRadiusAll(3);
        var barFill = new StyleBoxFlat { BgColor = fill };
        barFill.SetCornerRadiusAll(3);
        bar.AddThemeStyleboxOverride("background", track);
        bar.AddThemeStyleboxOverride("fill", barFill);
        row.AddChild(bar);

        var count = UiTheme.Text($"{done} / {of}", 11,
            bold ? UiTheme.TextHi : UiTheme.TextLo, HorizontalAlignment.Right);
        count.CustomMinimumSize = new Vector2(AchProgressCountWidth, 0);
        row.AddChild(count);
        return row;
    }

    private Control BuildReport()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);
        section.AddChild(col);

        col.AddChild(UiTheme.SectionTitle("Individual Report"));
        var report = achievement.Summary;
        col.AddChild(BuildReportRow("Accumulated Playtime",
            report.HasValue ? FormatPlayTime(report.Value.PlayMinutes) : "-"));
        col.AddChild(BuildReportRow("Monsters Defeated",
            report.HasValue ? $"{report.Value.MonstersDefeated:N0}" : "-"));
        col.AddChild(BuildReportRow("Players Defeated",
            report.HasValue ? $"{report.Value.PlayersDefeated:N0}" : "-"));
        col.AddChild(BuildReportRow("Deaths",
            report.HasValue ? $"{report.Value.Deaths:N0}" : "-"));
        return section;
    }

    private static Control BuildReportRow(string name, string value)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var label = UiTheme.Text(name, 12, UiTheme.TextLo);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        row.AddChild(UiTheme.Text(value, 12, UiTheme.TextHi, HorizontalAlignment.Right));
        return row;
    }

    private static string FormatPlayTime(int minutes)
    {
        if (minutes <= 0) return "00:00:00";
        return $"{minutes / 60:00}:{minutes % 60:00}:00";
    }

    private Control BuildRecent()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);
        section.AddChild(col);

        col.AddChild(UiTheme.SectionTitle("Recently Achieved"));

        int shown = 0;
        if (achievement.Summary is { } report)
        {
            foreach (int id in report.RecentlyAchieved)
            {
                if (id <= 0 || AchievementData.Get(id) is not { } info) continue;
                shown++;

                var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                row.AddThemeConstantOverride("separation", 6);

                var name = UiTheme.Text(info.Name, 12, UiTheme.GoldBright);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(name);
                row.AddChild(UiTheme.Text($"{info.Points}", 12, UiTheme.TextHi, HorizontalAlignment.Right));
                col.AddChild(row);

                var objective = UiTheme.Text(info.Objective, 11, UiTheme.TextLo);
                objective.CustomMinimumSize = new Vector2(AchRecentTextWidth, 0);
                objective.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                col.AddChild(objective);
            }
        }

        if (shown == 0)
            col.AddChild(UiTheme.Text("Nothing yet - go and earn something.", 12, UiTheme.TextLo));

        return section;
    }

    private void RefreshTabs()
    {
        if (GodotObject.IsInstanceValid(achSummaryTabButton))
        {
            achSummaryTabButton.AddThemeStyleboxOverride("normal", UiTheme.TopTab(ViewingSummary));
            achSummaryTabButton.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
            achSummaryTabButton.AddThemeStyleboxOverride("hover", UiTheme.TopTab(ViewingSummary, true));
            achSummaryTabButton.AddThemeColorOverride("font_color", UiTheme.Gold);
            achSummaryTabButton.AddThemeColorOverride("font_pressed_color", UiTheme.Gold);
            achSummaryTabButton.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        }

        foreach (var tab in achTabButtons)
        {
            var btn = tab.Value;
            if (!GodotObject.IsInstanceValid(btn)) continue;

            bool selected = !ViewingSummary && tab.Key == Tab;
            btn.AddThemeStyleboxOverride("normal", UiTheme.TopTab(selected));
            btn.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
            btn.AddThemeStyleboxOverride("hover", UiTheme.TopTab(selected, true));

            int waiting = achClaimablePerTab.GetValueOrDefault(tab.Key);
            string name = AchievementData.TabName(tab.Key);
            btn.Text = waiting > 0 ? $"{name} ({waiting})" : name;
            var tint = waiting > 0 ? UiTheme.GoldBright : UiTheme.Gold;
            btn.AddThemeColorOverride("font_color", tint);
            btn.AddThemeColorOverride("font_pressed_color", tint);
            btn.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        }
    }

    internal void RebuildList(bool keepScroll)
    {
        int scrollBack = keepScroll ? achScroll.ScrollVertical : 0;
        foreach (var child in achList.GetChildren()) child.QueueFree();
        RebuildCategories();

        achVisible.Clear();
        foreach (var entry in achievement.Entries)
        {
            if (AchievementData.Get(entry.Id) is not { } info) continue;
            if (Group == AchievementData.AllGroups ? info.Tab != Tab : info.Group != Group) continue;
            if (AchHideClaimed.ButtonPressed && entry.Claimed) continue;
            if (!AchievementFilter.Passes(StatusFilter, entry.Claimable || entry.Claimed)) continue;
            if (!AchievementFilter.Matches(info.Name, info.Objective, achSearch.Text)) continue;
            achVisible.Add(entry);
        }
        achVisible.Sort(CompareAchievements);

        foreach (var entry in achVisible)
        {
            if (AchievementData.Get(entry.Id) is not { } info) continue;
            achList.AddChild(BuildRow(entry, info));
        }

        if (achVisible.Count == 0 && achievement.Entries.Count > 0)
        {
            var empty = HudStyle.Label(13);
            empty.Text = achSearch.Text.Trim().Length > 0 || StatusFilter != AchievementStatusFilter.All
                ? $"No achievement in {AchievementData.TabName(Tab)} matches."
                : AchHideClaimed.ButtonPressed
                ? $"Everything in {AchievementData.TabName(Tab)} is claimed."
                : $"Nothing in {AchievementData.TabName(Tab)} yet.";
            achList.AddChild(empty);
        }

        achScroll.ScrollVertical = 0;
        if (scrollBack > 0)
            Callable.From(() => achScroll.ScrollVertical = scrollBack).CallDeferred();
    }

    private static int CompareAchievements(AchievementEntry a, AchievementEntry b)
    {
        int rank = Rank(a).CompareTo(Rank(b));
        if (rank != 0) return rank;

        if (Rank(a) == 1)
        {
            int fraction = b.Fraction.CompareTo(a.Fraction);
            if (fraction != 0) return fraction;
        }

        int groupA = AchievementData.Get(a.Id)?.Group ?? 0;
        int groupB = AchievementData.Get(b.Id)?.Group ?? 0;
        int group = groupA.CompareTo(groupB);
        return group != 0 ? group : a.Id.CompareTo(b.Id);
    }

    private static int Rank(AchievementEntry entry) =>
        entry.Claimable ? 0 : entry.Claimed ? 2 : 1;

    private Control BuildRow(AchievementEntry entry, AchievementData.Info info)
    {
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Row(muted: entry.Claimed));

        var margin = new MarginContainer();
        UiTheme.Margins(margin, 8, 5, 8, 5);
        row.AddChild(margin);

        var columns = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 8);
        margin.AddChild(columns);

        var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 1);
        columns.AddChild(text);

        var title = UiTheme.Text(info.Name, 13, entry.Claimed ? UiTheme.TextLo : UiTheme.TextHi);
        UiKit.Wrap(title);
        text.AddChild(title);

        if (info.Objective.Length > 0)
        {
            var objective = UiTheme.Text(info.Objective, 11, UiTheme.TextDim);
            UiKit.Wrap(objective);
            text.AddChild(objective);
        }

        text.AddChild(UiTheme.Text(
            $"{entry.ProgressText}   ·   {info.Points} pts",
            11, entry.Claimable ? UiTheme.Gold : UiTheme.TextDim));
        if (!entry.Claimed)
        {
            var bar = UiKit.HuntBar((int)(entry.Fraction * 100f), 100);
            bar.CustomMinimumSize = new Vector2(0, AchRowBarHeight);
            text.AddChild(bar);
        }

        if (info.TitleId != 0 && AchievementData.TitleOf(info.TitleId) is { } unlocked)
        {
            string bonus = unlocked.Bonus.Length > 0 ? $" — {unlocked.Bonus}" : "";
            var titleLine = UiTheme.Text($"Title: {unlocked.Name}{bonus}", 11,
                entry.Claimed ? UiTheme.GoldBright : UiTheme.TextDim);
            UiKit.Wrap(titleLine);
            text.AddChild(titleLine);
        }

        if (info.ItemId != 0 && ItemData.Get(info.ItemId) != null)
        {
            var reward = new ItemSlotView(AchRewardSlotSize) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            reward.Set(new ItemSlot { ItemId = info.ItemId, Count = (short)Math.Max(1, info.ItemCount), Durability = 1 });
            reward.Hovered += view => context.Tooltips.Show(-1, view.Item);
            reward.Unhovered += _ => context.Tooltips.Hide();
            columns.AddChild(reward);
        }
        columns.AddChild(BuildAction(entry, info));
        return row;
    }

    private Control BuildAction(AchievementEntry entry, AchievementData.Info info)
    {
        if (entry.Claimed && info.TitleId != 0 && AchievementData.TitleOf(info.TitleId) != null)
        {
            if (achievement.DisplayTitleId == info.TitleId)
                return UiTheme.Text("Worn", 12, UiTheme.Gold);
            int titleId = info.TitleId;
            var equip = new Button { Text = "Equip", FocusMode = Control.FocusModeEnum.None };
            equip.Pressed += () => requests.TitleSelect(titleId);
            return equip;
        }

        if (entry.Claimed)
            return UiTheme.Text("Claimed", 12, UiTheme.TextDim);

        if (!entry.Claimable)
            return UiTheme.Text($"{(int)(entry.Fraction * 100f)}%", 12, UiTheme.TextLo);

        int id = entry.Id;
        var claim = new Button { Text = "Claim", FocusMode = Control.FocusModeEnum.None };
        claim.Pressed += () =>
        {
            achClaimPending = id;
            requests.Claim(id);
        };
        return claim;
    }

    private const int TextItemIssued = 11411;
    private const int TextInventoryFull = 2301;
    private const int TextItemMissing = 19383;
    private const int TextErrorCode = 1517;

    private int achClaimPending;

    private void OnClaim(int achievementId, AchievementClaimResult result)
    {
        string text = result switch
        {
            AchievementClaimResult.Issued => GameText.System(TextItemIssued, "Item has been issued."),
            AchievementClaimResult.InventoryFull => GameText.System(TextInventoryFull, "You cannot pick up the item because your item inventory is full."),
            AchievementClaimResult.ItemMissing => GameText.System(TextItemMissing, "Item doesn't exist."),
            _ => GameText.System(TextErrorCode, "Error code (%d)", ((int)result).ToString()),
        };
        if (achievementId == achClaimPending)
        {
            achClaimPending = 0;
            Notice.Show(context.Root, text);
            return;
        }
        context.Log.Notice(result == AchievementClaimResult.Issued
            ? $"Achievement completed: {AchievementData.NameOf(achievementId)}."
            : text);
    }
}
