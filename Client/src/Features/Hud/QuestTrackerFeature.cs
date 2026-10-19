using Godot;

namespace LibreKO.Features.Hud;

internal sealed class QuestTrackerFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly QuestBook book;
    private readonly QuestProgressToastFeature progressToast;
    private readonly SelfSession self;

    public QuestTrackerFeature(IWorldContext context, QuestBook book, QuestProgressToastFeature progressToast, SelfSession self)
    {
        this.context = context;
        this.book = book;
        this.progressToast = progressToast;
        this.self = self;
    }

    internal event Action<int>? QuestPicked;

    public void Build()
    {
        context.Inventory.Refreshed += RefreshTracker;
        BuildTracker();
    }

    public void Dispose()
    {
        context.Inventory.Refreshed -= RefreshTracker;
    }

    private CanvasLayer trackerLayer = null!;
    internal PanelContainer TrackerPanel { get; private set; } = null!;
    private VBoxContainer trackerBox = null!;

    private static int TrackerPageSize => Platform.Pick(4, 1);

    private const int TrackerShortMax = 34;

    private static float TrackerPlatePadX => Platform.Pick(16f, 26f);

    private static float TrackerPlatePadY => Platform.Pick(8f, 14f);

    private int trackerPage;
    private int trackerPageCount = 1;
    private const string TrackerLayoutId = "quest_tracker";
    private const float TrackerHeaderHeight = 24f;
    private const float TrackerBarHeight = 3f;
    private PanelContainer? trackerHeader;
    private Label trackerCount = null!;
    private Button trackerFold = null!;

    private readonly record struct TrackerGoal(string Text, int Have, int Need);

    internal void BuildTracker()
    {
        trackerLayer = new CanvasLayer { Layer = UiLayers.HudTrackers };
        context.Root.AddChild(trackerLayer);
        context.Hud.PluginSeam(trackerLayer, LibreKO.Plugins.HudPart.QuestTracker);
        TrackerPanel = new PanelContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        TrackerPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        trackerLayer.AddChild(TrackerPanel);
        trackerBox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        trackerBox.AddThemeConstantOverride("separation", 5);
        TrackerPanel.AddChild(trackerBox);
        if (Platform.TouchUi)
            HudPlacement.QuestTracker.ApplyTo(TrackerPanel);
        else
        {
            trackerHeader = BuildTrackerHeader();
            HudLayout.Attach(TrackerPanel, TrackerLayoutId, trackerHeader, TrackerDefaultPosition);
        }
        RefreshTracker();
    }

    internal Vector2 TrackerDefaultPosition()
    {
        var viewport = TrackerPanel.GetViewportRect().Size;
        return new Vector2(viewport.X - HudAnchor.Edge - TrackerPanel.Size.X,
            Mathf.Max(HudAnchor.Edge, (viewport.Y - TrackerPanel.Size.Y) * 0.5f));
    }

    private PanelContainer BuildTrackerHeader()
    {
        var header = new PanelContainer
        {
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, TrackerHeaderHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "Drag to move",
        };
        header.AddThemeStyleboxOverride("panel", UiTheme.HeaderBand(2));
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        header.AddChild(row);
        var title = TrackerText("Quests", 12, UiTheme.GoldBright);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(title);
        trackerCount = TrackerText("", 11, UiTheme.TextLo);
        trackerCount.VerticalAlignment = VerticalAlignment.Center;
        trackerCount.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        trackerCount.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(trackerCount);
        trackerFold = UiTheme.IconButton(Config.QuestTrackerCollapsed ? "▸" : "▾", "Collapse or expand");
        trackerFold.CustomMinimumSize = new Vector2(22, 20);
        trackerFold.Pressed += () =>
        {
            Config.SetQuestTrackerCollapsed(!Config.QuestTrackerCollapsed);
            trackerFold.Text = Config.QuestTrackerCollapsed ? "▸" : "▾";
            RefreshTracker();
        };
        row.AddChild(trackerFold);
        return header;
    }

    private List<QuestEntry> TrackedQuests()
    {
        var active = new List<QuestEntry>();
        foreach (var q in book.Entries)
        {
            if (!Config.LegacyQuestFallback && !book.Views.ContainsKey(q.QuestId)) continue;
            if (q.State != QuestStates.Active && q.State != QuestStates.ReadyToTurnIn) continue;
            if (book.Tracked.Count > 0 && !book.Tracked.Contains(q.QuestId)) continue;
            active.Add(q);
        }
        return active;
    }

    internal List<LibreKO.Plugins.GameQuestTrack> TrackedQuestsForPlugins()
    {
        string selfName = self.LastEnter.Name ?? "";
        var list = new List<LibreKO.Plugins.GameQuestTrack>();
        foreach (var q in TrackedQuests())
        {
            bool done = q.State == QuestStates.ReadyToTurnIn;
            var lines = done ? new List<string> { "Ready to turn in" } : KillProgressLines(q.QuestId);
            if (lines.Count == 0) lines.Add(ShortObjective(q.QuestId, selfName));
            list.Add(new LibreKO.Plugins.GameQuestTrack(q.QuestId, QuestInfo.Name(book, q.QuestId, selfName), done, lines));
        }
        return list;
    }

    internal void RefreshTracker()
    {
        context.Plugins.Quests();
        if (trackerBox == null) return;
        foreach (var c in trackerBox.GetChildren())
        {
            trackerBox.RemoveChild(c);
            if (c != trackerHeader) c.QueueFree();
        }
        string selfName = self.LastEnter.Name ?? "";

        var active = TrackedQuests();

        progressToast.ReportProgress();

        trackerPageCount = Math.Max(1, (active.Count + TrackerPageSize - 1) / TrackerPageSize);
        trackerPage = Math.Clamp(trackerPage, 0, trackerPageCount - 1);

        if (trackerHeader != null)
        {
            trackerCount.Text = $"({active.Count})";
            trackerBox.AddChild(trackerHeader);
            if (Config.QuestTrackerCollapsed)
            {
                TrackerPanel.Visible = active.Count > 0;
                return;
            }
        }

        int start = trackerPage * TrackerPageSize;
        int end = Math.Min(active.Count, start + TrackerPageSize);
        for (int i = start; i < end; i++)
            trackerBox.AddChild(Platform.TouchUi
                ? BuildTouchTracker(active[i], selfName)
                : BuildTrackerQuest(active[i], selfName));

        if (!Platform.TouchUi && trackerPageCount > 1)
        {
            trackerBox.AddChild(BuildTrackerPager());
        }
        TrackerPanel.Visible = active.Count > 0;
        Callable.From(FitTracker).CallDeferred();
    }

    private void FitTracker()
    {
        if (!GodotObject.IsInstanceValid(TrackerPanel) || TrackerPanel.GetParent() != trackerLayer) return;
        if (Platform.TouchUi) HudPlacement.QuestTracker.ApplyTo(TrackerPanel);
        else TrackerPanel.ResetSize();
    }

    private Control BuildTouchTracker(QuestEntry q, string selfName)
    {
        bool done = q.State == QuestStates.ReadyToTurnIn;
        int questId = q.QuestId;

        var button = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
        };
        var plate = new StyleBoxFlat
        {
            BgColor = new Color(0.020f, 0.023f, 0.030f, 0.72f),
            BorderColor = new Color(done ? UiTheme.Good : UiTheme.Edge, 0.34f),
        };
        plate.SetCornerRadiusAll(5);
        plate.SetBorderWidthAll(1);
        plate.ContentMarginLeft = plate.ContentMarginRight = TrackerPlatePadX;
        plate.ContentMarginTop = plate.ContentMarginBottom = TrackerPlatePadY;
        var hover = (StyleBoxFlat)plate.Duplicate();
        hover.BgColor = new Color(0.055f, 0.060f, 0.070f, 0.86f);
        button.AddThemeStyleboxOverride("normal", plate);
        button.AddThemeStyleboxOverride("pressed", hover);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("focus", plate);

        var label = TrackerText(done ? "Ready to turn in" : ShortObjective(questId, selfName),
                                13, done ? UiTheme.Good : Colors.White);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(label);
        button.CustomMinimumSize = label.GetMinimumSize()
            + new Vector2(TrackerPlatePadX * 2f, TrackerPlatePadY * 2f);

        button.Pressed += () => QuestPicked?.Invoke(questId);
        return button;
    }

    private string ShortObjective(int questId, string selfName)
    {
        var kills = KillProgressLines(questId);
        if (kills.Count > 0) return Clip($"Kill {kills[0]}");
        return Clip(GoalLines(questId, selfName)[0]);
    }

    private List<string> GoalLines(int questId, string selfName)
    {
        var lines = new List<string>();
        foreach (var want in book.HandIns(questId))
            lines.Add($"Bring {ItemData.DisplayName(want.ItemId)} "
                      + $"{Mathf.Min(self.Inventory.CountOf(want.ItemId), want.Count)}/{want.Count}");
        if (lines.Count > 0) return lines;

        var npcs = book.Views.TryGetValue(questId, out var view)
            ? new[] { view.NpcId } : QuestData.NpcsForState(questId, QuestStates.Active);
        if (npcs.Length > 0)
        {
            lines.Add($"Talk to {QuestInfo.NpcDisplayName(npcs[0])}");
            return lines;
        }

        string text = QuestInfo.Journal(book, questId, selfName);
        if (string.IsNullOrWhiteSpace(text)) text = QuestInfo.Name(book, questId, selfName);
        int newline = text.IndexOf('\n');
        lines.Add((newline >= 0 ? text[..newline] : text).Trim());
        return lines;
    }

    private static string Clip(string text)
    {
        int newline = text.IndexOf('\n');
        if (newline >= 0) text = text[..newline].TrimEnd();
        return text.Length > TrackerShortMax
            ? text[..TrackerShortMax].TrimEnd() + "\u2026"
            : text;
    }

    private Control BuildTrackerQuest(QuestEntry q, string selfName)
    {
        var box = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        box.AddThemeConstantOverride("separation", 2);

        var titlePanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, 24),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        titlePanel.AddThemeStyleboxOverride("panel", UiTheme.Row(q.State == 3, true));
        int questId = q.QuestId;
        titlePanel.MouseFilter = Control.MouseFilterEnum.Stop;
        titlePanel.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        titlePanel.TooltipText = "Open in the quest journal";
        titlePanel.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            QuestPicked?.Invoke(questId);
        };
        box.AddChild(titlePanel);

        var titleRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleRow.AddThemeConstantOverride("separation", 6);
        titlePanel.AddChild(titleRow);

        var icon = TrackerText(q.State == 3 ? "✓" : "◇", 11,
            q.State == 3 ? UiTheme.Good : UiTheme.GoldBright, HorizontalAlignment.Center);
        icon.CustomMinimumSize = new Vector2(14, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        titleRow.AddChild(icon);

        var title = TrackerText(QuestInfo.Name(book, q.QuestId, selfName),
            12, q.State == 3 ? UiTheme.Good : Colors.White);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        title.CustomMinimumSize = new Vector2(MiniMap.SquareSize - 38f, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        titleRow.AddChild(title);

        var goalBox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        goalBox.AddThemeConstantOverride("separation", 1);
        box.AddChild(goalBox);

        if (q.State == 3)
        {
            goalBox.AddChild(TrackerObjective("Ready to turn in", UiTheme.Good));
        }
        else
        {
            foreach (var goal in TrackerGoals(q.QuestId, selfName))
            {
                if (string.IsNullOrWhiteSpace(goal.Text)) continue;
                goalBox.AddChild(TrackerObjective(goal.Text, goal.Need > 0 && goal.Have >= goal.Need ? UiTheme.Good : Colors.White));
                if (goal.Need > 0) goalBox.AddChild(TrackerBar(goal.Have, goal.Need));
            }
        }

        return box;
    }

    private List<TrackerGoal> TrackerGoals(int questId, string selfName)
    {
        var goals = new List<TrackerGoal>();
        foreach (var (mob, current, target) in QuestInfo.KillProgress(book, questId))
            goals.Add(new TrackerGoal($"Kill {mob} {current}/{target}", current, target));
        if (goals.Count > 0) return goals;
        foreach (var want in book.HandIns(questId))
        {
            int have = Mathf.Min(self.Inventory.CountOf(want.ItemId), want.Count);
            goals.Add(new TrackerGoal($"Bring {ItemData.DisplayName(want.ItemId)} {have}/{want.Count}", have, want.Count));
        }
        if (goals.Count > 0) return goals;
        foreach (var line in GoalLines(questId, selfName))
            goals.Add(new TrackerGoal(line.StartsWith("Talk to") ? line : $"Note: {line}", 0, 0));
        return goals;
    }

    private static Control TrackerBar(int have, int need)
    {
        var margin = new MarginContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        margin.AddThemeConstantOverride("margin_left", (int)TrackerPlatePadX / 2);
        margin.AddThemeConstantOverride("margin_right", (int)TrackerPlatePadX / 2);
        margin.AddChild(UiKit.HuntBar(have, need));
        margin.GetChild<Control>(0).CustomMinimumSize = new Vector2(0, TrackerBarHeight);
        return margin;
    }

    private Control BuildTrackerPager()
    {
        var pager = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(MiniMap.SquareSize, 24),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        pager.AddThemeConstantOverride("separation", 5);

        var prev = UiTheme.IconButton("<", "Previous quest page");
        prev.CustomMinimumSize = new Vector2(28, 22);
        prev.AddThemeFontSizeOverride("font_size", 12);
        prev.Disabled = trackerPage <= 0;
        prev.Pressed += () =>
        {
            trackerPage = Math.Max(0, trackerPage - 1);
            RefreshTracker();
        };
        pager.AddChild(prev);

        var page = TrackerText($"{trackerPage + 1}/{trackerPageCount}", 10, UiTheme.TextHi,
            HorizontalAlignment.Center);
        page.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        page.VerticalAlignment = VerticalAlignment.Center;
        page.MouseFilter = Control.MouseFilterEnum.Ignore;
        pager.AddChild(page);

        var next = UiTheme.IconButton(">", "Next quest page");
        next.CustomMinimumSize = new Vector2(28, 22);
        next.AddThemeFontSizeOverride("font_size", 12);
        next.Disabled = trackerPage >= trackerPageCount - 1;
        next.Pressed += () =>
        {
            trackerPage = Math.Min(trackerPageCount - 1, trackerPage + 1);
            RefreshTracker();
        };
        pager.AddChild(next);

        return pager;
    }

    private static Label TrackerText(string text, int rawSize, Color color,
        HorizontalAlignment align = HorizontalAlignment.Left)
    {
        int size = (int)(rawSize * HudPlacement.TrackerFontScale);
        var label = UiTheme.Text(text, size, color, align);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
        label.AddThemeConstantOverride("outline_size", 5);
        return label;
    }

    private static Label TrackerObjective(string text, Color color)
    {
        var label = TrackerText("  " + text, 11, color);
        label.CustomMinimumSize = new Vector2(MiniMap.SquareSize, 0);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    private List<string> KillProgressLines(int questId)
    {
        var lines = new List<string>();
        foreach (var (mob, current, target) in QuestInfo.KillProgress(book, questId)) lines.Add($"{mob} {current}/{target}");
        return lines;
    }
}
