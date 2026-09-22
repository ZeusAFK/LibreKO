using System.Collections.Generic;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const float TrophyIconSize = 20f;
    private const float TrophyBadgeSize = 13f;
    private const float TrophyTouchBadgeSize = 30f;
    private const int TrophyTouchCountFont = 17;
    private const int TrophyCountFont = 9;
    private const float TrophyGap = 6f;
    private const float TrophyPadX = 9f;
    private const float TrophyBlinkDim = 0.3f;
    private const float TrophyBlinkStep = 0.6f;

    private static float BadgeSizeForTrophy =>
        Platform.TouchUi ? TrophyTouchBadgeSize : TrophyBadgeSize;

    private static Color TrophyIconColor(bool waiting) => Platform.TouchUi
        ? (waiting ? UiTheme.GoldBright : UiTheme.Gold)
        : (waiting ? UiTheme.GoldBright : UiTheme.Gold);

    private CanvasLayer _trophyLayer = null!;
    private Button _trophy = null!;
    private TextureRect _trophyIcon = null!;
    private PanelContainer _trophyBadge = null!;
    private Label _trophyCount = null!;
    private Tween? _trophyBlink;

    private const int AchListWidth = 520;
    private const int AchListHeight = 360;
    private const int AchScoreCardWidth = 118;
    private const int AchProgressLabelWidth = 74;
    private const int AchProgressCountWidth = 62;
    private const int AchRecentTextWidth = 210;

    private CanvasLayer _achLayer = null!;
    private HudWindow _achPanel = null!;
    private ScrollContainer _achScroll = null!;
    private VBoxContainer _achList = null!;
    private HBoxContainer _achTabs = null!;
    private Label _achSummary = null!;
    private CheckButton _achHideClaimed = null!;
    private bool _achShown;

    private bool _achOnSummary = true;
    private AchievementSummary _achReport;
    private bool _achHasReport;
    private VBoxContainer _achSummaryPage = null!;
    private Button _achSummaryTabButton = null!;
    private AchievementTab _achTab = AchievementTab.War;
    private readonly List<AchievementEntry> _achEntries = new();
    private readonly List<AchievementEntry> _achVisible = new();
    private readonly Dictionary<AchievementTab, Button> _achTabButtons = new();
    private readonly Dictionary<AchievementTab, int> _achClaimablePerTab = new();

    private void AchievementInit()
    {
        _achLayer = new CanvasLayer { Layer = 73 };
        AddChild(_achLayer);
        _achPanel = new HudWindow("achievements", "Achievements", new Vector2(160, 110),
            bodyMinWidth: AchListWidth)
        { Visible = false };
        _achPanel.Closed += CloseAchievements;
        _achLayer.AddChild(_achPanel);

        var root = _achPanel.Body;
        root.AddThemeConstantOverride("separation", 6);

        _achTabs = new HBoxContainer();
        _achTabs.AddThemeConstantOverride("separation", 4);
        root.AddChild(_achTabs);

        _achSummaryTabButton = UiTheme.TopTabButton("Summary");
        _achSummaryTabButton.Pressed += ShowAchievementSummaryPage;
        _achTabs.AddChild(_achSummaryTabButton);

        foreach (var category in AchievementData.Tabs)
        {
            var value = category;
            var btn = UiTheme.TopTabButton(AchievementData.TabName(category));
            btn.Pressed += () => SelectAchievementTab(value);
            _achTabButtons[category] = btn;
            _achTabs.AddChild(btn);
        }

        var summaryRow = new HBoxContainer();
        summaryRow.AddThemeConstantOverride("separation", 10);
        root.AddChild(summaryRow);

        _achSummary = HudStyle.Label(13);
        _achSummary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        summaryRow.AddChild(_achSummary);

        _achHideClaimed = new CheckButton { Text = "Hide claimed", FocusMode = Control.FocusModeEnum.None };
        _achHideClaimed.AddThemeFontSizeOverride("font_size", 12);
        _achHideClaimed.Toggled += _ => RebuildAchievementList(keepScroll: false);
        summaryRow.AddChild(_achHideClaimed);

        _achScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(AchListWidth, AchListHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        root.AddChild(_achScroll);
        _achList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _achList.AddThemeConstantOverride("separation", 3);
        _achScroll.AddChild(_achList);

        _achSummaryPage = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(AchListWidth, AchListHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _achSummaryPage.AddThemeConstantOverride("separation", 8);
        root.AddChild(_achSummaryPage);

        BuildTrophy();

        Net.I.AchievementListEvent += OnAchievementList;
        Net.I.AchievementSummaryEvent += OnAchievementSummary;
        Net.I.AchievementClaimEvent += OnAchievementClaim;
        Net.I.TitleChangedEvent += OnEntityTitle;
        ApplyAchievementPageVisibility();
        RefreshAchievementCounts();
        RebuildAchievementList(keepScroll: false);
        RebuildAchievementSummary();
        Net.I.SendAchievementList();
        Net.I.SendAchievementSummary();
    }

    private void AchievementDispose()
    {
        Net.I.AchievementListEvent -= OnAchievementList;
        Net.I.AchievementSummaryEvent -= OnAchievementSummary;
        Net.I.AchievementClaimEvent -= OnAchievementClaim;
        Net.I.TitleChangedEvent -= OnEntityTitle;
        if (_trophyBlink != null && _trophyBlink.IsValid()) _trophyBlink.Kill();
        _trophyBlink = null;
    }

    private void ToggleAchievements()
    {
        if (_achShown) { CloseAchievements(); return; }
        _achPanel.Visible = true;
        _achShown = true;
        RebuildAchievementList(keepScroll: false);
        Net.I.SendAchievementList();
        Net.I.SendAchievementSummary();
    }

    private void CloseAchievements()
    {
        if (!_achShown) return;
        _achShown = false;
        _achPanel.Visible = false;
    }

    private void SelectAchievementTab(AchievementTab category)
    {
        if (_achTab == category && !_achOnSummary) return;
        _achTab = category;
        _achOnSummary = false;
        ApplyAchievementPageVisibility();
        RefreshAchievementCounts();
        RebuildAchievementList(keepScroll: false);
    }

    private void ShowAchievementSummaryPage()
    {
        if (_achOnSummary) return;
        _achOnSummary = true;
        ApplyAchievementPageVisibility();
        RefreshAchievementCounts();
        RebuildAchievementSummary();
    }

    private void ApplyAchievementPageVisibility()
    {
        _achScroll.Visible = !_achOnSummary;
        _achSummaryPage.Visible = _achOnSummary;
        _achHideClaimed.Visible = !_achOnSummary;
        _achSummary.Visible = !_achOnSummary;
    }

    private void OnAchievementSummary(AchievementSummary summary)
    {
        _achReport = summary;
        _achHasReport = true;
        if (_achShown) RebuildAchievementSummary();
    }

    private void OnAchievementList(List<AchievementEntry> list)
    {
        foreach (var entry in list)
        {
            int index = _achEntries.FindIndex(known => known.Id == entry.Id);
            if (index >= 0) _achEntries[index] = entry;
            else _achEntries.Add(entry);
        }

        RefreshAchievementCounts();
        if (_achShown) RebuildAchievementList(keepScroll: true);
    }

    private void RefreshAchievementCounts()
    {
        _achClaimablePerTab.Clear();
        int claimed = 0, points = 0, claimable = 0;
        int inCategory = 0, claimedInCategory = 0;

        foreach (var entry in _achEntries)
        {
            if (AchievementData.Get(entry.Id) is not { } info) continue;
            bool earned = entry.Claimed || entry.Claimable;
            if (earned) { claimed++; points += info.Points; }
            if (entry.Claimable)
            {
                claimable++;
                _achClaimablePerTab[info.Tab] =
                    _achClaimablePerTab.GetValueOrDefault(info.Tab) + 1;
            }
            if (info.Tab != _achTab) continue;
            inCategory++;
            if (earned) claimedInCategory++;
        }

        RefreshTrophy(claimable);
        RefreshAchievementTabs();
        if (_titleShown) RebuildTitleList();

        _achSummary.Text = _achEntries.Count == 0
            ? "Waiting for the server…"
            : $"{AchievementData.TabName(_achTab)} {claimedInCategory} / {inCategory}"
              + $"   ·   {claimed} of {_achEntries.Count} earned   ·   {points} points";
    }

    private void RebuildAchievementSummary()
    {
        foreach (var child in _achSummaryPage.GetChildren())
            child.QueueFree();

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 8);
        _achSummaryPage.AddChild(top);
        top.AddChild(BuildAchievementScoreCard());
        top.AddChild(BuildAchievementTabProgress());

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 8);
        bottom.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _achSummaryPage.AddChild(bottom);
        bottom.AddChild(BuildAchievementReport());
        bottom.AddChild(BuildAchievementRecent());
    }

    private Control BuildAchievementScoreCard()
    {
        var section = UiTheme.Section();
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(AchScoreCardWidth, 0) };
        col.AddThemeConstantOverride("separation", 2);
        section.AddChild(col);

        col.AddChild(UiTheme.Text("Achievement Points", 11, UiTheme.TextLo, HorizontalAlignment.Center));
        col.AddChild(UiTheme.Heading(26, _achHasReport ? $"{_achReport.Points:N0}" : "-"));
        return section;
    }

    private Control BuildAchievementTabProgress()
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
            foreach (var entry in _achEntries)
            {
                if (AchievementData.Get(entry.Id) is not { } info || info.Tab != tab) continue;
                of++;
                if (entry.Claimed || entry.Claimable) done++;
            }

            doneTotal += done;
            ofTotal += of;
            rows.Add((AchievementData.TabName(tab), done, of));
        }

        col.AddChild(BuildAchievementProgressRow("Total", doneTotal, ofTotal, UiTheme.GoldBright, bold: true));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        foreach (var row in rows)
            col.AddChild(BuildAchievementProgressRow(row.Name, row.Done, row.Of, UiTheme.Bronze));

        return section;
    }

    private static Control BuildAchievementProgressRow(
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

    private Control BuildAchievementReport()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);
        section.AddChild(col);

        col.AddChild(UiTheme.SectionTitle("Individual Report"));
        col.AddChild(BuildAchievementReportRow("Accumulated Playtime",
            _achHasReport ? FormatPlayTime(_achReport.PlayMinutes) : "-"));
        col.AddChild(BuildAchievementReportRow("Monsters Defeated",
            _achHasReport ? $"{_achReport.MonstersDefeated:N0}" : "-"));
        col.AddChild(BuildAchievementReportRow("Players Defeated",
            _achHasReport ? $"{_achReport.PlayersDefeated:N0}" : "-"));
        col.AddChild(BuildAchievementReportRow("Deaths",
            _achHasReport ? $"{_achReport.Deaths:N0}" : "-"));
        return section;
    }

    private static Control BuildAchievementReportRow(string name, string value)
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

    private Control BuildAchievementRecent()
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);
        section.AddChild(col);

        col.AddChild(UiTheme.SectionTitle("Recently Achieved"));

        int shown = 0;
        if (_achHasReport)
        {
            foreach (int id in _achReport.RecentlyAchieved)
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

    private void RefreshAchievementTabs()
    {
        if (GodotObject.IsInstanceValid(_achSummaryTabButton))
        {
            _achSummaryTabButton.AddThemeStyleboxOverride("normal", UiTheme.TopTab(_achOnSummary));
            _achSummaryTabButton.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
            _achSummaryTabButton.AddThemeStyleboxOverride("hover", UiTheme.TopTab(_achOnSummary, true));
            _achSummaryTabButton.AddThemeColorOverride("font_color", UiTheme.Gold);
            _achSummaryTabButton.AddThemeColorOverride("font_pressed_color", UiTheme.Gold);
            _achSummaryTabButton.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        }

        foreach (var tab in _achTabButtons)
        {
            var btn = tab.Value;
            if (!GodotObject.IsInstanceValid(btn)) continue;

            bool selected = !_achOnSummary && tab.Key == _achTab;
            btn.AddThemeStyleboxOverride("normal", UiTheme.TopTab(selected));
            btn.AddThemeStyleboxOverride("pressed", UiTheme.TopTab(true));
            btn.AddThemeStyleboxOverride("hover", UiTheme.TopTab(selected, true));

            int waiting = _achClaimablePerTab.GetValueOrDefault(tab.Key);
            string name = AchievementData.TabName(tab.Key);
            btn.Text = waiting > 0 ? $"{name} ({waiting})" : name;
            var tint = waiting > 0 ? UiTheme.GoldBright : UiTheme.Gold;
            btn.AddThemeColorOverride("font_color", tint);
            btn.AddThemeColorOverride("font_pressed_color", tint);
            btn.AddThemeColorOverride("font_hover_color", UiTheme.GoldBright);
        }
    }

    private void RebuildAchievementList(bool keepScroll)
    {
        int scrollBack = keepScroll ? _achScroll.ScrollVertical : 0;
        foreach (var child in _achList.GetChildren()) child.QueueFree();

        _achVisible.Clear();
        foreach (var entry in _achEntries)
        {
            if (AchievementData.Get(entry.Id) is not { } info) continue;
            if (info.Tab != _achTab) continue;
            if (_achHideClaimed.ButtonPressed && entry.Claimed) continue;
            _achVisible.Add(entry);
        }
        _achVisible.Sort(CompareAchievements);

        foreach (var entry in _achVisible)
        {
            if (AchievementData.Get(entry.Id) is not { } info) continue;
            _achList.AddChild(BuildAchievementRow(entry, info));
        }

        if (_achVisible.Count == 0 && _achEntries.Count > 0)
        {
            var empty = HudStyle.Label(13);
            empty.Text = _achHideClaimed.ButtonPressed
                ? $"Everything in {AchievementData.TabName(_achTab)} is claimed."
                : $"Nothing in {AchievementData.TabName(_achTab)} yet.";
            _achList.AddChild(empty);
        }

        _achScroll.ScrollVertical = 0;
        if (scrollBack > 0)
            Callable.From(() => _achScroll.ScrollVertical = scrollBack).CallDeferred();
    }

    private static int CompareAchievements(AchievementEntry a, AchievementEntry b)
    {
        int rank = AchievementRank(a).CompareTo(AchievementRank(b));
        if (rank != 0) return rank;

        if (AchievementRank(a) == 1)
        {
            int fraction = b.Fraction.CompareTo(a.Fraction);
            if (fraction != 0) return fraction;
        }

        int groupA = AchievementData.Get(a.Id)?.Group ?? 0;
        int groupB = AchievementData.Get(b.Id)?.Group ?? 0;
        int group = groupA.CompareTo(groupB);
        return group != 0 ? group : a.Id.CompareTo(b.Id);
    }

    private static int AchievementRank(AchievementEntry entry) =>
        entry.Claimable ? 0 : entry.Claimed ? 2 : 1;

    private Control BuildAchievementRow(AchievementEntry entry, AchievementData.Info info)
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
        Wrap(title);
        text.AddChild(title);

        if (info.Objective.Length > 0)
        {
            var objective = UiTheme.Text(info.Objective, 11, UiTheme.TextDim);
            Wrap(objective);
            text.AddChild(objective);
        }

        text.AddChild(UiTheme.Text(
            $"{entry.ProgressText}   ·   {info.Points} pts",
            11, entry.Claimable ? UiTheme.Gold : UiTheme.TextDim));

        if (info.TitleId != 0 && AchievementData.TitleOf(info.TitleId) is { } unlocked)
        {
            string bonus = unlocked.Bonus.Length > 0 ? $" — {unlocked.Bonus}" : "";
            var titleLine = UiTheme.Text($"Title: {unlocked.Name}{bonus}", 11,
                entry.Claimed ? UiTheme.GoldBright : UiTheme.TextDim);
            Wrap(titleLine);
            text.AddChild(titleLine);
        }

        columns.AddChild(BuildAchievementAction(entry, info));
        return row;
    }

    private static void Wrap(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(1, 0);
    }

    private Control BuildAchievementAction(AchievementEntry entry, AchievementData.Info info)
    {
        if (entry.Claimed)
            return UiTheme.Text("Claimed", 12, UiTheme.TextDim);

        if (!entry.Claimable)
            return UiTheme.Text($"{(int)(entry.Fraction * 100f)}%", 12, UiTheme.TextLo);

        int id = entry.Id;
        var claim = new Button { Text = "Claim", FocusMode = Control.FocusModeEnum.None };
        claim.Pressed += () => Net.I.SendAchievementClaim(id);
        return claim;
    }

    private void OnAchievementClaim(int achievementId, int result)
    {
        if (result == Net.AchievementClaimIssued)
        {
            CombatNotice($"{AchievementData.NameOf(achievementId)} — reward received.");
            return;
        }

        CombatNotice(result switch
        {
            Net.AchievementClaimInventoryFull => "Your inventory is too full for that reward.",
            Net.AchievementClaimItemMissing => "That reward no longer exists.",
            Net.AchievementClaimNotAvailable => "That achievement is not complete yet.",
            _ => $"The reward could not be issued. ({result})",
        });
    }

    private void BuildTrophy()
    {
        _trophyLayer = new CanvasLayer { Layer = 66 };
        AddChild(_trophyLayer);

        _trophy = new Button { FocusMode = Control.FocusModeEnum.None, TooltipText = "Achievements" };
        var flat = new StyleBoxEmpty();
        _trophy.AddThemeStyleboxOverride("normal", flat);
        _trophy.AddThemeStyleboxOverride("hover", flat);
        _trophy.AddThemeStyleboxOverride("pressed", flat);
        _trophy.Pressed += ToggleAchievements;
        _trophy.MouseEntered += () => _trophyIcon.SelfModulate = UiTheme.GoldBright;
        _trophy.MouseExited += () => _trophyIcon.SelfModulate = TrophyIconColor(_trophyBadge?.Visible == true);
        _trophyLayer.AddChild(_trophy);

        _trophyIcon = UiIcons.Image("system/trophy",
            new Vector2(TrophyIconSize, TrophyIconSize), TrophyIconColor(false));
        _trophyIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
        _trophyIcon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _trophy.AddChild(_trophyIcon);

        var badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        badge.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        badge.AddChild(new BadgeDisc { MouseFilter = Control.MouseFilterEnum.Ignore });
        badge.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        badge.OffsetLeft = -BadgeSizeForTrophy;
        badge.OffsetTop = -BadgeSizeForTrophy;
        badge.OffsetRight = 2;
        badge.OffsetBottom = 2;
        _trophy.AddChild(badge);

        _trophyCount = UiTheme.Text("",
            Platform.TouchUi ? TrophyTouchCountFont : TrophyCountFont,
            UiTheme.Self, HorizontalAlignment.Center);
        _trophyCount.VerticalAlignment = VerticalAlignment.Center;
        _trophyCount.MouseFilter = Control.MouseFilterEnum.Ignore;
        badge.AddChild(_trophyCount);
        _trophyBadge = badge;
        _trophyBadge.Visible = false;

        if (_attendanceGift != null) _attendanceGift.Resized += PlaceTrophy;
        if (_premiumChip != null) _premiumChip.Resized += PlaceTrophy;
        _trophy.Resized += PlaceTrophy;
        Callable.From(PlaceTrophy).CallDeferred();
    }

    private void PlaceTrophy()
    {
        if (Platform.TouchUi)
        {
            float side = HudPlacement.LauncherButtonSize;
            _trophy.CustomMinimumSize = new Vector2(side, side);
            float inset = side * HudPlacement.LauncherGlyphInset;
            _trophyIcon.OffsetLeft = _trophyIcon.OffsetTop = inset;
            _trophyIcon.OffsetRight = _trophyIcon.OffsetBottom = -inset;
            HudPlacement.AchievementTrophy.ApplyTo(_trophy);
            return;
        }
        if (_premiumChip == null) return;

        float giftWidth = _attendanceGift?.Size.X ?? 0f;
        _trophy.CustomMinimumSize = new Vector2(
            TrophyIconSize + TrophyPadX * 2f,
            Mathf.Max(_premiumChip.Size.Y, TrophyIconSize));
        HudAnchor.Pin(_trophy, HudAnchor.Spot.TopRight, new Vector2(
            HudAnchor.Edge + MiniMap.SquareSize + StatusHudGap
                + _premiumChip.Size.X + TrophyGap + giftWidth + TrophyGap,
            HudAnchor.Edge));
        PlaceTopRightMailbox();
    }

    private void RefreshTrophy(int claimable)
    {
        if (_trophy == null || !GodotObject.IsInstanceValid(_trophy)) return;

        bool waiting = claimable > 0;
        _trophyBadge.Visible = waiting;
        _trophyCount.Text = claimable > 9 ? "9+" : claimable.ToString();
        _trophyIcon.SelfModulate = TrophyIconColor(waiting);
        _trophy.TooltipText = waiting
            ? $"Achievements — {claimable} reward{(claimable == 1 ? "" : "s")} to claim"
            : "Achievements";

        if (_trophyBlink != null && _trophyBlink.IsValid()) _trophyBlink.Kill();
        _trophyBlink = null;
        _trophy.Modulate = Colors.White;
        if (!waiting) return;

        var blink = _trophy.CreateTween().SetLoops();
        blink.TweenProperty(_trophy, "modulate:a", TrophyBlinkDim, TrophyBlinkStep);
        blink.TweenProperty(_trophy, "modulate:a", 1f, TrophyBlinkStep);
        _trophyBlink = blink;
    }

}
