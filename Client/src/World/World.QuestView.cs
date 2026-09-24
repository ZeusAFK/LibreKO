using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private readonly Dictionary<int, QuestView> _questViews = new();
    private int _questRewardChoice = -1;
    private bool _questUiRefreshQueued;

    private void QueueQuestUiRefresh()
    {
        if (_questUiRefreshQueued) return;
        _questUiRefreshQueued = true;
        Callable.From(() =>
        {
            _questUiRefreshQueued = false;
            if (!IsInsideTree()) return;
            RefreshQuestLog();
            RefreshTracker();
            RefreshNpcQuestMarkers();
        }).CallDeferred();
    }

    private void OnQuestView(QuestView view)
    {
        _questViews[view.QuestId] = view;
        _questRewardChoice = -1;
        _questStrings[view.QuestId] = new QuestStrings(view.QuestId, view.Title, view.Journal);
        _questObjectives[view.QuestId] = view.Objectives;
        _questKills[view.QuestId] = view.Counts;
        var state = view.State switch
        {
            QuestViewState.InProgress => 1,
            QuestViewState.Claimable => 3,
            QuestViewState.Completed => 2,
            _ => 0
        };
        var index = _quests.FindIndex(q => q.QuestId == view.QuestId);
        if (index < 0) _quests.Add(new QuestEntry(view.QuestId, state));
        else _quests[index] = new QuestEntry(view.QuestId, state);
        QueueQuestUiRefresh();
        if (_questNotifications.Any(q => q.QuestId == view.QuestId && q.State != view.State)
            || view.ZoneId != 0 && view.ZoneId != _zone)
        {
            _questNotifications.RemoveAll(q => q.QuestId == view.QuestId);
            RefreshQuestNotification();
        }
        if (view.Notification)
            ShowQuestNotification(view);
        else if (view.Open && view.Page == QuestPageKind.Conversation)
            ShowQuestConversation(view);
        else if (view.Open)
            ShowQuestView(view);
    }

    private void ShowQuestConversation(QuestView view)
    {
        BeginNpcDialog(GameData.I != null ? GameData.I.NpcName(view.NpcId, false) : $"NPC #{view.NpcId}",
            view.Dialogue);
        int shown = 0;
        for (var index = 0; index < view.Topics.Length; index++)
        {
            var offered = index;
            AddNpcMenuButton($"{++shown}.   {view.Topics[index]}", () => OnNpcMenuClick(offered));
        }
        AddNpcMenuButton($"{++shown}.   Close", CloseNpcDialog);
        EndNpcDialog(shown);
    }

    private void ShowQuestView(QuestView view)
    {
        BeginNpcDialog(view.Title, "");
        _npcBody.GetParent<Control>().Visible = false;
        _npcQuestScroll.Visible = true;
        _npcQuestScroll.ScrollVertical = 0;
        foreach (var child in _npcQuestContent.GetChildren()) { _npcQuestContent.RemoveChild(child); child.QueueFree(); }
        _npcQuestContent.AddChild(QuestParagraph(
            view.Dialogue.Length > 0 ? view.Dialogue : view.Journal, UiTheme.TextHi));

        if (view.Objectives.Groups.Length > 0)
        {
            var hunt = QuestSection(QuestObjectiveHeading(view.Objectives.Groups.Length > 0, false));
            if (view.Objectives.AnyWillDo)
                hunt.AddChild(UiTheme.Text("Complete any one", 12, UiTheme.TextLo));
            for (var index = 0; index < view.Objectives.Groups.Length; index++)
            {
                var goal = view.Objectives.Groups[index];
                var name = goal.Name ?? (GameData.I != null ? GameData.I.NpcName(goal.Monsters[0], true) : "Creature");
                hunt.AddChild(QuestObjectiveTarget(QuestValueRow(name, $"{view.Counts[index]} / {goal.Count}",
                    view.Counts[index] >= goal.Count ? UiTheme.Good : UiTheme.GoldBright),
                    view.QuestId, index, goal.HasTarget));
            }
        }
        var deliveries = view.Transfers.Where(t => t.Take).ToArray();
        if (deliveries.Length > 0)
        {
            var collect = QuestSection(QuestObjectiveHeading(false, true));
            foreach (var transfer in deliveries)
            {
                var held = transfer.Kind switch
                {
                    0 => Inv.CountOf(transfer.ItemId),
                    1 => Sheet.Gold,
                    _ => -1
                };
                collect.AddChild(QuestItemRow(transfer.DisplayItemId, QuestTransferName(transfer),
                    held < 0 ? $"× {transfer.Count:n0}" : $"{Mathf.Min(held, transfer.Count):n0} / {transfer.Count:n0}",
                    held >= transfer.Count ? UiTheme.Good : UiTheme.GoldBright));
            }
        }
        if (view.Objectives.Groups.Length == 0 && deliveries.Length == 0)
            QuestSection("Objectives").AddChild(QuestParagraph(view.StandingObjective, UiTheme.TextLo));

        var payouts = view.Transfers.Where(t => !t.Take).ToArray();
        if (payouts.Length > 0)
        {
            var rewards = QuestSection("Rewards");
            foreach (var transfer in payouts)
                rewards.AddChild(QuestItemRow(transfer.DisplayItemId, QuestTransferName(transfer),
                    transfer.Kind is 4 or 5 ? "" : transfer.Count.ToString("n0"), UiTheme.GoldBright));
        }
        System.Action? onRewardChosen = null;
        if (view.Options.Length > 0)
        {
            var choice = QuestSection("Choose one");
            var marks = new List<Label>();
            for (var index = 0; index < view.Options.Length; index++)
            {
                var option = view.Options[index];
                var row = QuestItemRow(option.DisplayItemId, QuestTransferName(option),
                    $"{option.Count:n0}  ○", UiTheme.GoldBright);
                marks.Add(row.GetChild<Label>(row.GetChildCount() - 1));
                choice.AddChild(QuestRewardOption(row, index, () => onRewardChosen?.Invoke()));
            }
            void PaintChoice()
            {
                for (var index = 0; index < marks.Count; index++)
                {
                    var chosen = index == _questRewardChoice;
                    marks[index].Text = $"{view.Options[index].Count:n0}  {(chosen ? "●" : "○")}";
                    marks[index].AddThemeColorOverride("font_color", chosen ? UiTheme.Good : UiTheme.GoldBright);
                }
            }
            onRewardChosen = PaintChoice;
            PaintChoice();
        }
        if (view.Daily && view.State == QuestViewState.Completed && view.NextReset > 0)
            _npcQuestContent.AddChild(QuestParagraph($"Available again: {DateTimeOffset.FromUnixTimeSeconds(view.NextReset).ToLocalTime():g}", UiTheme.TextLo));

        for (var index = 0; index < view.Topics.Length; index++)
        {
            var offered = index;
            AddNpcMenuButton(view.Topics[index], () => OnNpcMenuClick(offered));
        }
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        _npcMenuBox.AddChild(actions);
        Button Action(string label, System.Action run, bool enabled = true)
        {
            var button = new Button { Text = label, CustomMinimumSize = new Vector2(0, 36), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Disabled = !enabled };
            if (!enabled) button.TooltipText = "Choose a reward first";
            button.Pressed += () => { CloseNpcDialog(); run(); };
            actions.AddChild(button);
            return button;
        }
        if (view.CanAccept) Action("Accept", () => Net.I.SendQuestAccept(view.QuestId));
        if (view.CanClaim)
        {
            var confirm = Action("Confirm", () => Net.I.SendQuestComplete(view.QuestId, _questRewardChoice),
                view.Options.Length == 0 || _questRewardChoice >= 0);
            var paint = onRewardChosen;
            onRewardChosen = () =>
            {
                paint?.Invoke();
                confirm.Disabled = _questRewardChoice < 0;
                confirm.TooltipText = confirm.Disabled ? "Choose a reward first" : "";
            };
        }
        if (!view.CanClaim && view.State is QuestViewState.InProgress or QuestViewState.Claimable)
            Action("Abandon", () => Net.I.SendQuestAbandon(view.QuestId));
        Action(view.CanAccept ? "Reject" : "Close", () => { });
        EndNpcDialog(view.Topics.Length + 1);
        Callable.From(FitQuestPanel).CallDeferred();
    }

    private void FitQuestPanel()
    {
        if (!IsInstanceValid(_npcPanel) || !_npcQuestScroll.Visible) return;
        var viewport = _npcPanel.GetViewportRect().Size;
        var room = Mathf.Max(100, viewport.Y - 80 - _npcMenuScroll.CustomMinimumSize.Y);
        _npcQuestScroll.CustomMinimumSize = new Vector2(0, Mathf.Min(_npcQuestContent.GetCombinedMinimumSize().Y, room));
        _npcPanel.ResetSize();
        _npcPanel.Position = new Vector2(Mathf.Clamp(_npcPanel.Position.X, 0, Mathf.Max(0, viewport.X - _npcPanel.Size.X)),
            Mathf.Clamp(_npcPanel.Position.Y, 0, Mathf.Max(0, viewport.Y - _npcPanel.Size.Y)));
    }

    private VBoxContainer QuestSection(string title) => QuestSection(_npcQuestContent, title);

    private static VBoxContainer QuestSection(Control parent, string title)
    {
        parent.AddChild(UiTheme.Text(title, 13, UiTheme.Gold));
        var panel = UiTheme.Section();
        parent.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);
        return body;
    }

    private const float QuestParagraphWidth = 400f;

    internal static string QuestObjectiveHeading(bool kills, bool deliveries) =>
        kills ? "Hunt" : deliveries ? "Collect" : "Objectives";

    private static RichTextLabel QuestParagraph(string text, Color color, float width = QuestParagraphWidth)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            Text = QuestMarkup.Rich(text, Net.I?.LastEnter.Name ?? ""),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(width, 0)
        };
        label.AddThemeFontSizeOverride("normal_font_size", 14);
        label.AddThemeColorOverride("default_color", color);
        return label;
    }

    private Control QuestRewardOption(Control row, int index, System.Action chosen)
    {
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.TooltipText = "Pick this reward";
        row.GuiInput += ev =>
        {
            if (ev is not InputEventMouseButton { ButtonIndex: MouseButton.Left } click) return;
            row.AcceptEvent();
            if (!click.Pressed) return;
            _questRewardChoice = index;
            chosen();
        };
        return row;
    }

    private Control QuestObjectiveTarget(Control row, int questId, int group, bool hasTarget)
    {
        if (!hasTarget) return row;
        row.MouseFilter = Control.MouseFilterEnum.Stop;
        row.TooltipText = "Show where to find this";
        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Net.I.SendQuestTargetRequest(questId, group);
        };
        return row;
    }

    private const float QuestRowIconSide = 32f;

    private Control QuestItemRow(int itemId, string name, string value, Color color)
    {
        var row = QuestValueRow(name, value, color);
        var icon = new TextureRect
        {
            Texture = ItemData.Icon(itemId),
            CustomMinimumSize = new Vector2(QuestRowIconSide, QuestRowIconSide),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        row.AddChild(icon);
        row.MoveChild(icon, 0);
        return QuestItemHover(row, itemId);
    }

    private static bool HasItemCard(int itemId) =>
        !QuestData.IsVirtualReward(itemId) && ItemData.Get(itemId) != null;

    private T QuestItemHover<T>(T control, int itemId) where T : Control
    {
        if (!HasItemCard(itemId)) return control;
        control.MouseFilter = Control.MouseFilterEnum.Pass;
        control.MouseEntered += () => ShowItemTooltip(-1, TooltipItem(itemId));
        control.MouseExited += HideItemTooltip;
        return control;
    }

    private static HBoxContainer QuestValueRow(string name, string value, Color color)
    {
        var row = new HBoxContainer();
        var label = UiTheme.Text(name, 14, UiTheme.TextHi);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(label);
        row.AddChild(UiTheme.Text(value, 14, color, HorizontalAlignment.Right));
        return row;
    }

    private static string QuestTransferName(QuestTransfer transfer) => transfer.Kind switch
    {
        4 => "First job change",
        5 => "Second job change",
        _ => QuestRewardName(transfer.DisplayItemId)
    };

    private static string QuestRewardName(int displayItemId) => displayItemId switch
    {
        QuestData.CoinItemId => "Coins",
        QuestData.ExpItemId => "Experience",
        QuestData.LadderPointItemId => "Ladder points",
        _ => ItemData.DisplayName(displayItemId)
    };

    private IEnumerable<(int ItemId, int Count)> QuestHandIns(int questId) =>
        _questViews.TryGetValue(questId, out var view)
            ? view.Transfers.Where(t => t.Take && t.Kind == 0).Select(t => (t.ItemId, t.Count))
            : QuestData.HandIns(questId).Select(t => (t.ItemId, t.Count));

    private IEnumerable<(int ItemId, int Count)> QuestRewards(int questId) =>
        _questViews.TryGetValue(questId, out var view)
            ? view.Transfers.Where(t => !t.Take).Concat(view.Options).Select(t => (t.DisplayItemId, t.Count))
            : QuestData.Rewards(questId, _selfClass).Select(t => (t.ItemId, t.Count));
}
