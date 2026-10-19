using Godot;

namespace LibreKO.Features.Windows;

internal sealed partial class QuestFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly NpcQuestSession npc;
    private readonly QuestBook book;
    private readonly SelfSession self;
    private readonly INpcQuestRequests npcRequests;
    private readonly QuestMarkersFeature markers;
    private readonly MapFeature map;
    private readonly QuestTrackerFeature tracker;

    public QuestFeature(IWorldContext context, NpcQuestSession npcQuest, QuestBook questBook, SelfSession self, INpcQuestRequests npcQuestRequests, QuestMarkersFeature markers, MapFeature map, QuestTrackerFeature tracker)
    {
        this.context = context;
        npc = npcQuest;
        book = questBook;
        this.self = self;
        npcRequests = npcQuestRequests;
        this.markers = markers;
        this.map = map;
        this.tracker = tracker;
    }

    private const int FilterAvailable = 0;
    private const int FilterActive = 1;
    private const int FilterCompleted = 2;
    private const float DetailWidth = 244f;
    private const float ListWidth = 408f;

    internal VBoxContainer QuestsContent { get; private set; } = null!;
    private VBoxContainer listBox = null!;
    private ScrollContainer listScroll = null!;
    private string search = "";
    private bool scrollTop;
    private int filter = FilterActive;
    private QuestData.Kind? kind;

    internal int Selected { get; set; } = -1;

    private readonly Dictionary<string, Button> subBtns = new();

    private readonly Dictionary<string, Button> kindBtns = new();
    private Label detailTitle = null!;
    private Label detailSub = null!;
    private Label detailFacts = null!;
    private RichTextLabel detailDesc = null!;
    private VBoxContainer objectiveBox = null!;
    private Label objectiveTitle = null!;
    private Label rewardTitle = null!;
    private HFlowContainer rewardBox = null!;
    private Label rewardOptionTitle = null!;
    private HBoxContainer rewardOptionBox = null!;
    private Control detailBody = null!;
    private Label detailEmpty = null!;
    private Button abandonBtn = null!;
    private Button trackBtn = null!;
    private Button completeBtn = null!;

    public void Build()
    {
        tracker.QuestPicked += OnTrackerPick;
        BuildLog();

        npc.QuestLogEvent += OnLog;
        npc.QuestStateEvent += OnState;
        npc.QuestKillCountsEvent += OnKillCounts;
        npc.QuestObjectivesEvent += OnObjectives;
        npc.QuestStringsEvent += OnStrings;
        npc.QuestViewEvent += OnView;
        npc.QuestReceiptEvent += OnReceipt;
        npc.QuestKillUpdateEvent += OnKillUpdate;
        npc.QuestRewardRefusedEvent += OnRewardRefused;

        npcRequests.QuestLogRequest();
    }

    public void Dispose()
    {
        tracker.QuestPicked -= OnTrackerPick;
        npc.QuestLogEvent -= OnLog;
        npc.QuestStateEvent -= OnState;
        npc.QuestKillCountsEvent -= OnKillCounts;
        npc.QuestObjectivesEvent -= OnObjectives;
        npc.QuestStringsEvent -= OnStrings;
        npc.QuestViewEvent -= OnView;
        npc.QuestReceiptEvent -= OnReceipt;
        npc.QuestKillUpdateEvent -= OnKillUpdate;
        npc.QuestRewardRefusedEvent -= OnRewardRefused;
    }

    private const int TextRewardWeight = 7432;
    private const int TextRewardCoins = 7433;
    private const int TextRewardBagFull = 7434;

    private void OnRewardRefused(QuestRewardRefusal reason) => Notice.Show(context.Root, reason switch
    {
        QuestRewardRefusal.WeightExceeded => ItemData.Text(TextRewardWeight,
            "You are not eligible for a reward as your weight limitation has been exceeded."),
        QuestRewardRefusal.CoinsExceeded => ItemData.Text(TextRewardCoins,
            "You are not eligible for a reward as your Noah limitation has been exceeded."),
        _ => ItemData.Text(TextRewardBagFull, "You are not eligible for a reward as your bag is full."),
    });

    internal void BuildLog()
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 7);
        QuestsContent = root;

        var statusBar = new HBoxContainer();
        statusBar.AddThemeConstantOverride("separation", 1);
        root.AddChild(statusBar);
        var statusGroup = new ButtonGroup();
        foreach (var (label, filter) in new[]
                 {
                     ("In Progress", FilterActive),
                     ("Available", FilterAvailable),
                     ("Completed", FilterCompleted),
                 })
        {
            int f = filter;
            var b = UiTheme.TopTabButton(label, 13);
            b.ButtonGroup = statusGroup;
            b.Pressed += () => SetFilter(f);
            subBtns[label] = b;
            statusBar.AddChild(b);
        }

        var kindBar = new HBoxContainer();
        kindBar.AddThemeConstantOverride("separation", 1);
        root.AddChild(kindBar);
        var kindGroup = new ButtonGroup();
        foreach (var (label, kind) in new (string, QuestData.Kind?)[]
                 {
                     ("All", null),
                     ("Story", QuestData.Kind.Story),
                     ("Hunt", QuestData.Kind.Hunt),
                     ("Delivery", QuestData.Kind.Delivery),
                 })
        {
            var k = kind;
            var b = UiTheme.TopTabButton(label, 11);
            b.ButtonGroup = kindGroup;
            b.Pressed += () => SetKind(k);
            kindBtns[label] = b;
            kindBar.AddChild(b);
        }
        var searchInput = new LineEdit
        {
            PlaceholderText = "Search quests",
            ClearButtonEnabled = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        searchInput.AddThemeFontSizeOverride("font_size", 12);
        searchInput.TextChanged += text =>
        {
            search = text;
            scrollTop = true;
            RefreshLog();
        };
        kindBar.AddChild(searchInput);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        root.AddChild(body);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(ListWidth, 372),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.AddChild(scroll);
        listScroll = scroll;
        listBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        listBox.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(listBox);

        body.AddChild(BuildDetail());

        var footer = UiTheme.Text(
            "Click a quest for its details  ·  tick its box to track it on screen  ·  click a pinned objective to see where it is",
            11, UiTheme.TextDim);
        root.AddChild(footer);

        SetFilter(FilterActive);
        SetKind(null);
    }

    private Control BuildDetail()
    {
        var panel = UiTheme.Section();
        panel.CustomMinimumSize = new Vector2(DetailWidth + 24f, 0);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 7);
        panel.AddChild(box);

        detailEmpty = UiTheme.Text("Select a quest to see its details.", 12, UiTheme.TextDim,
            HorizontalAlignment.Center);
        detailEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailEmpty.CustomMinimumSize = new Vector2(DetailWidth, 0);
        box.AddChild(detailEmpty);

        var detail = new VBoxContainer { Visible = false };
        detail.AddThemeConstantOverride("separation", 7);
        box.AddChild(detail);
        detailBody = detail;

        detailTitle = UiTheme.Text("", 16, UiTheme.GoldBright);
        detailTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailTitle.CustomMinimumSize = new Vector2(DetailWidth, 0);
        detail.AddChild(detailTitle);

        detailSub = UiTheme.Text("", 11, UiTheme.TextDim);
        detail.AddChild(detailSub);

        detailFacts = UiTheme.Text("", 11, UiTheme.TextLo);
        detailFacts.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailFacts.CustomMinimumSize = new Vector2(DetailWidth, 0);
        detail.AddChild(detailFacts);

        detailDesc = QuestRows.Paragraph("", UiTheme.TextLo, self.LastEnter.Name, DetailWidth);
        detailDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        detailDesc.CustomMinimumSize = new Vector2(DetailWidth, 0);
        detail.AddChild(detailDesc);

        detail.AddChild(new HSeparator());
        objectiveTitle = UiTheme.SectionTitle("Objectives");
        detail.AddChild(objectiveTitle);
        objectiveBox = new VBoxContainer();
        objectiveBox.AddThemeConstantOverride("separation", 3);
        detail.AddChild(objectiveBox);

        rewardTitle = UiTheme.SectionTitle("Rewards");
        detail.AddChild(rewardTitle);
        rewardBox = new HFlowContainer { CustomMinimumSize = new Vector2(DetailWidth, 0) };
        rewardBox.AddThemeConstantOverride("h_separation", 5);
        rewardBox.AddThemeConstantOverride("v_separation", 5);
        detail.AddChild(rewardBox);
        rewardOptionTitle = UiTheme.SectionTitle("Choose one");
        detail.AddChild(rewardOptionTitle);
        rewardOptionBox = new HBoxContainer();
        rewardOptionBox.AddThemeConstantOverride("separation", 5);
        detail.AddChild(rewardOptionBox);

        detail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 5);
        detail.AddChild(buttons);
        completeBtn = ActionButton("Turn in", "Talk to the quest NPC, then turn in your completed objectives");
        completeBtn.Pressed += CompleteSelected;
        buttons.AddChild(completeBtn);
        abandonBtn = ActionButton("Abandon", "Give up this quest");
        abandonBtn.Pressed += AbandonSelected;
        buttons.AddChild(abandonBtn);
        trackBtn = ActionButton("Track", "Show this quest in the on-screen tracker");
        trackBtn.Pressed += ToggleTrackSelected;
        buttons.AddChild(trackBtn);

        return panel;
    }

    private static Button ActionButton(string text, string tooltip)
    {
        var b = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = tooltip,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        b.AddThemeFontSizeOverride("font_size", 12);
        return b;
    }

    private void SetFilter(int filter)
    {
        this.filter = filter;
        string label = filter switch
        {
            FilterCompleted => "Completed",
            FilterAvailable => "Available",
            _ => "In Progress",
        };
        if (subBtns.TryGetValue(label, out var b)) b.ButtonPressed = true;
        RefreshLog();
    }

    private void SetKind(QuestData.Kind? kind)
    {
        this.kind = kind;
        string label = kind?.ToString() ?? "All";
        if (kindBtns.TryGetValue(label, out var b)) b.ButtonPressed = true;
        RefreshLog();
    }

    private bool InFilter(int state) => filter switch
    {
        FilterActive => state is QuestStates.Active or QuestStates.ReadyToTurnIn,
        FilterCompleted => state == QuestStates.Completed,
        FilterAvailable => state == QuestStates.Abandoned,
        _ => false,
    };

    private List<(int QuestId, int State)> QuestsInView()
    {
        var rows = new List<(int, int)>();
        foreach (var q in book.Entries)
            if (InFilter(q.State) && (Config.LegacyQuestFallback || book.Views.ContainsKey(q.QuestId)))
                rows.Add((q.QuestId, q.State));

        if (filter == FilterAvailable)
        {
            foreach (int id in QuestData.Startable(book.StateOf, self.Sheet.Level, self.Class, self.LastEnter.Nation))
                if (!book.Views.ContainsKey(id)) rows.Add((id, 0));
            foreach (var view in book.Views.Values)
                if (view.State == QuestViewState.Available) rows.Add((view.QuestId, 0));
        }

        if (kind is { } kindFilter)
            rows.RemoveAll(r => QuestInfo.Facts(book, r.Item1).Kind != kindFilter);

        if (search.Length > 0)
        {
            string selfName = self.LastEnter.Name ?? "";
            rows.RemoveAll(r => !QuestJournalRules.Matches(QuestInfo.Name(book, r.Item1, selfName), search));
        }

        rows.Sort((a, b) => QuestJournalRules.Compare(JournalRow(a.Item1, a.Item2), JournalRow(b.Item1, b.Item2)));
        return rows;
    }

    private QuestJournalRules.Row JournalRow(int questId, int state)
    {
        var facts = QuestInfo.Facts(book, questId);
        return new QuestJournalRules.Row(questId, state, facts.Zone, facts.Level);
    }

    internal void RefreshLog()
    {
        if (listBox == null) return;
        int keepScroll = scrollTop ? 0 : listScroll.ScrollVertical;
        scrollTop = false;
        foreach (var c in listBox.GetChildren()) { listBox.RemoveChild(c); c.QueueFree(); }
        string selfName = self.LastEnter.Name ?? "";

        var rows = QuestsInView();
        var zoneCounts = new Dictionary<int, int>();
        foreach (var (questId, _) in rows)
        {
            int zone = QuestInfo.Facts(book, questId).Zone;
            zoneCounts[zone] = zoneCounts.GetValueOrDefault(zone) + 1;
        }
        int lastZone = int.MinValue;
        foreach (var (questId, state) in rows)
        {
            int zone = QuestInfo.Facts(book, questId).Zone;
            if (zone != lastZone)
            {
                lastZone = zone;
                listBox.AddChild(ZoneHeader(zone, zoneCounts[zone]));
            }
            listBox.AddChild(BuildRow(questId, state, selfName));
        }

        if (rows.Count == 0)
        {
            var hint = UiTheme.Text(
                search.Length > 0 ? "No quest matches your search." : filter switch
                {
                    FilterCompleted => "Nothing finished yet.",
                    FilterAvailable => "No quests are open to you right now.",
                    _ => "No quests in progress.\nTalk to NPCs to find some.",
                },
                13, UiTheme.TextLo, HorizontalAlignment.Center);
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            hint.CustomMinimumSize = new Vector2(ListWidth - 16f, 0);
            listBox.AddChild(hint);
        }

        if (Selected >= 0 && rows.FindIndex(r => r.QuestId == Selected) < 0)
            Selected = -1;

        RefreshDetail();
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(listScroll)) listScroll.ScrollVertical = keepScroll;
        }).CallDeferred();
    }

    private Control ZoneHeader(int zone, int count)
    {
        var panel = new PanelContainer();
        var band = new StyleBoxFlat { BgColor = new Color(0.105f, 0.108f, 0.125f, 0.95f) };
        band.SetCornerRadiusAll(2);
        band.ContentMarginLeft = 8;
        band.ContentMarginRight = 8;
        band.ContentMarginTop = band.ContentMarginBottom = 4;
        panel.AddThemeStyleboxOverride("panel", band);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        panel.AddChild(row);
        var caret = UiTheme.Text("▼", 8, new Color(UiTheme.TextLo, 0.7f));
        caret.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(caret);
        row.AddChild(UiTheme.Text(QuestJournalRules.GroupTitle(QuestInfo.ZoneName(zone), count), 12, UiTheme.TextLo));
        return panel;
    }

    private Control BuildRow(int questId, int state, string selfName)
    {
        bool isSelected = questId == Selected;
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        panel.AddThemeStyleboxOverride("panel", UiTheme.ListRow(isSelected));
        panel.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Select(questId);
        };

        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 6);
        panel.AddChild(row);

        var glyph = UiTheme.Text(
            state == QuestStates.ReadyToTurnIn ? "✓" : state == QuestStates.Completed ? "✓" : "◆",
            12, QuestInfo.StateColor(state), HorizontalAlignment.Center);
        glyph.CustomMinimumSize = new Vector2(16, 0);
        glyph.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(glyph);

        var text = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        text.AddThemeConstantOverride("separation", -1);
        row.AddChild(text);

        var name = UiTheme.Text(QuestInfo.Name(book, questId, selfName), 13,
            isSelected ? UiTheme.GoldBright : QuestInfo.StateColor(state));
        name.ClipText = true;
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        text.AddChild(name);

        var facts = QuestInfo.Facts(book, questId);
        var status = UiTheme.Text(QuestJournalRules.StatusLine(facts.Level, QuestInfo.StatusText(book, questId, state)), 10,
            UiTheme.TextDim);
        status.ClipText = true;
        status.MouseFilter = Control.MouseFilterEnum.Ignore;
        text.AddChild(status);

        var (current, target) = QuestInfo.Progress(book, questId);
        var progress = UiTheme.Text(target > 0 ? $"{current} / {target}" : "", 11,
            target > 0 && current >= target ? UiTheme.Good : UiTheme.Gold, HorizontalAlignment.Right);
        progress.CustomMinimumSize = new Vector2(48, 0);
        progress.MouseFilter = Control.MouseFilterEnum.Ignore;
        row.AddChild(progress);

        var track = UiTheme.FlatCheck("", book.Tracked.Contains(questId));
        track.TooltipText = "Track this quest on screen";
        track.Disabled = state is not (QuestStates.Active or QuestStates.ReadyToTurnIn);
        track.Toggled += on => SetTracked(questId, on);
        row.AddChild(track);

        return panel;
    }

    private void OnTrackerPick(int questId)
    {
        context.MainWindows.Show("Quests");
        Select(questId);
    }

    internal void Select(int questId)
    {
        Selected = questId;
        Audio.PlayUi(Sfx.UiButton);
        RefreshLog();
    }

    private void SetTracked(int questId, bool tracked)
    {
        if (!book.Track(questId, tracked)) return;
        tracker.RefreshTracker();
        RefreshDetail();
    }

    private void ToggleTrackSelected()
    {
        if (Selected < 0) return;
        SetTracked(Selected, !book.Tracked.Contains(Selected));
        RefreshLog();
    }

    private void AbandonSelected()
    {
        if (Selected < 0) return;
        npcRequests.QuestAbandon(Selected);
    }

    private void CompleteSelected()
    {
        if (Selected < 0 || book.StateOf(Selected) != QuestStates.ReadyToTurnIn) return;
        npcRequests.QuestComplete(Selected);
    }

    private void RefreshDetail()
    {
        if (detailBody == null) return;
        bool has = Selected >= 0;
        detailBody.Visible = has;
        detailEmpty.Visible = !has;
        if (!has) return;

        string selfName = self.LastEnter.Name ?? "";
        int questId = Selected;
        int state = book.StateOf(questId);
        var facts = QuestInfo.Facts(book, questId);

        detailTitle.Text = QuestInfo.Name(book, questId, selfName);
        var sub = new List<string> { facts.Kind.ToString() };
        if (facts.Level > 0) sub.Add($"Lv. {facts.Level}");
        sub.Add(book.Views.TryGetValue(questId, out var view) ? view.StateLabel : QuestInfo.StateName(state));
        detailSub.Text = string.Join("  ·  ", sub);
        detailFacts.Text = PlaceFacts(questId, state, facts.Zone);
        detailFacts.Visible = detailFacts.Text.Length > 0;
        detailDesc.Text = QuestMarkup.Rich(QuestInfo.JournalSource(book, questId, selfName), selfName);

        foreach (var c in objectiveBox.GetChildren()) { objectiveBox.RemoveChild(c); c.QueueFree(); }
        objectiveTitle.Text = QuestRows.ObjectiveHeading(
            QuestInfo.Groups(book, questId).Length > 0, book.HandIns(questId).Any());
        if (book.Objectives.TryGetValue(questId, out var objectives) && objectives.AnyWillDo)
            objectiveBox.AddChild(ObjectiveRow("Complete any one:"));
        var kills = QuestInfo.KillProgress(book, questId);
        for (var group = 0; group < kills.Count; group++)
        {
            bool pinned = book.Views.TryGetValue(questId, out var goals) && group < goals.Objectives.Groups.Length
                && goals.Objectives.Groups[group].HasTarget;
            var (mob, current, target) = kills[group];
            objectiveBox.AddChild(QuestRows.ObjectiveTarget(
                ObjectiveProgress($"{mob} {current}/{target}", current, target, pinned), questId, group, pinned, npcRequests));
        }
        foreach (var want in book.HandIns(questId))
        {
            int have = Mathf.Min(self.Inventory.CountOf(want.ItemId), want.Count);
            objectiveBox.AddChild(ObjectiveProgress(
                $"{ItemData.DisplayName(want.ItemId)} {have}/{want.Count}", have, want.Count, false));
        }
        if (objectiveBox.GetChildCount() == 0)
            objectiveBox.AddChild(ObjectiveRow(state switch
            {
                QuestStates.ReadyToTurnIn => "Ready to turn in",
                QuestStates.Completed => "You have finished this quest.",
                _ => TalkToLine(questId, state),
            }));

        foreach (var c in rewardBox.GetChildren()) { rewardBox.RemoveChild(c); c.QueueFree(); }
        foreach (var c in rewardOptionBox.GetChildren()) { rewardOptionBox.RemoveChild(c); c.QueueFree(); }
        foreach (var reward in book.Rewards(questId, self.Class))
            rewardBox.AddChild(RewardTile(reward.ItemId, reward.Count));
        rewardTitle.Visible = rewardBox.GetChildCount() > 0;
        var options = book.Views.TryGetValue(questId, out var rewardView) ? rewardView.Options : [];
        var chosen = book.RewardSelection.Chosen(questId, out var pending) && options.Contains(pending);
        foreach (var option in chosen ? new[] { pending } : options)
            rewardOptionBox.AddChild(RewardTile(option.DisplayItemId, option.Count));
        rewardOptionTitle.Visible = rewardOptionBox.GetChildCount() > 0;

        trackBtn.Text = book.Tracked.Contains(questId) ? "Untrack" : "Track";
        trackBtn.Disabled = state is not (QuestStates.Active or QuestStates.ReadyToTurnIn);
        abandonBtn.Disabled = state is not (QuestStates.Active or QuestStates.ReadyToTurnIn)
            || book.Views.TryGetValue(questId, out var autoView) && autoView.AutoAccepted;
        completeBtn.Disabled = book.Views.TryGetValue(questId, out var actionView)
            ? !actionView.CanClaim || actionView.Options.Length > 0
            : state != QuestStates.ReadyToTurnIn;
    }

    private string PlaceFacts(int questId, int state, int zone)
    {
        var parts = new List<string>(2);
        if (zone != QuestJournalRules.AnywhereZone) parts.Add($"Area: {QuestInfo.ZoneName(zone)}");
        var npcs = book.Views.TryGetValue(questId, out var view)
            ? new[] { view.NpcId } : QuestData.NpcsForState(questId, state);
        if (npcs.Length > 0 && npcs[0] > 0) parts.Add($"NPC: {QuestInfo.NpcDisplayName(npcs[0])}");
        return string.Join("   ·   ", parts);
    }

    private string TalkToLine(int questId, int state)
    {
        var npcs = book.Views.TryGetValue(questId, out var view)
            ? new[] { view.NpcId } : QuestData.NpcsForState(questId, state);
        if (npcs.Length == 0) return "See the quest text";
        string who = QuestInfo.NpcDisplayName(npcs[0]);
        return state == 0 ? $"Ask {who} for this quest" : $"Report to {who}";
    }

    private Control RewardTile(int itemId, int count)
    {
        var box = QuestRows.Hover(context.Tooltips, 
            new VBoxContainer { TooltipText = QuestRows.HasItemCard(itemId) ? "" : ItemData.DisplayName(itemId) }, itemId);
        box.AddThemeConstantOverride("separation", 3);

        var tile = new PanelContainer { CustomMinimumSize = new Vector2(52, 52) };
        tile.AddThemeStyleboxOverride("panel", UiTheme.Slot());
        var icon = ItemIcons.Get(itemId);
        if (icon != null)
        {
            var rect = new TextureRect
            {
                Texture = icon,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            tile.AddChild(rect);
            UpgradeBadge.Show(tile, itemId);
        }
        else if (VirtualIcon(itemId) is { } virtualIcon)
        {
            tile.AddChild(new TextureRect
            {
                Texture = UiIcons.Get(virtualIcon),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = UiTheme.Gold,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            box.TooltipText = $"{UiKit.QuestRewardName(itemId)}  {count:n0}";
        }
        else
        {
            var glyph = UiTheme.Text("?", 15, UiTheme.TextLo, HorizontalAlignment.Center);
            glyph.VerticalAlignment = VerticalAlignment.Center;
            tile.AddChild(glyph);
        }
        box.AddChild(tile);

        var caption = UiTheme.Text(QuestJournalRules.CompactCount(count), 10, UiTheme.TextHi, HorizontalAlignment.Center);
        caption.CustomMinimumSize = new Vector2(52, 0);
        caption.ClipText = true;
        box.AddChild(caption);
        return box;
    }

    private static string? VirtualIcon(int itemId) => itemId switch
    {
        QuestData.CoinItemId => "system/coins",
        QuestData.ExpItemId => "system/level",
        QuestData.LadderPointItemId => "system/medal",
        QuestData.JobChangeItemId => "system/star",
        _ => null,
    };

    private static Control ObjectiveProgress(string text, int current, int target, bool pinned)
    {
        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
        box.AddThemeConstantOverride("separation", 2);
        var row = ObjectiveRow(text);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        if (pinned)
            row.AddChild(new TextureRect
            {
                Texture = UiIcons.Get("system/pin"),
                CustomMinimumSize = new Vector2(PinSize, PinSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SelfModulate = UiTheme.Gold,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        box.AddChild(row);
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = QuestJournalRules.Fraction(current, target),
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, ObjectiveBarHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bar.AddThemeStyleboxOverride("background", UiTheme.MeterTrack());
        bar.AddThemeStyleboxOverride("fill", UiTheme.MeterFill(current >= target ? UiTheme.Good : UiTheme.Gold));
        box.AddChild(bar);
        return box;
    }

    private const float PinSize = 12f;
    private const float ObjectiveBarHeight = 3f;

    private static Control ObjectiveRow(string text)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var bullet = UiTheme.Text("◆", 10, UiTheme.Gold);
        bullet.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(bullet);
        var label = UiTheme.Text(text, 12, UiTheme.TextLo);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.CustomMinimumSize = new Vector2(DetailWidth - 20f, 0);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(label);
        return row;
    }

    private void OnLog(List<QuestEntry> entries) => RefreshAll();

    private void OnState(int questId, int state) => RefreshAll();

    private void OnKillCounts(int questId, ushort[] counts) => RefreshProgress();

    private void OnObjectives(QuestObjectives objectives) => RefreshProgress();

    private void OnStrings(List<QuestStrings> texts) => RefreshProgress();

    private void OnKillUpdate(int questId, int group, int count) => RefreshProgress();

    private bool viewRefreshQueued;

    internal void OnView(QuestView view)
    {
        if (viewRefreshQueued) return;
        viewRefreshQueued = true;
        Callable.From(() =>
        {
            viewRefreshQueued = false;
            if (!context.Root.IsInsideTree()) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            RefreshAll();
            Diag.Hitch("quest refresh", started);
        }).CallDeferred();
    }

    private void RefreshAll()
    {
        markers.RefreshNpcMarkers();
        RefreshProgress();
    }

    private void RefreshProgress()
    {
        if (context.MainWindows.IsOpen("Quests")) RefreshLog();
        tracker.RefreshTracker();
    }

    private void OnReceipt(QuestReceipt receipt) => RefreshDetail();
}
