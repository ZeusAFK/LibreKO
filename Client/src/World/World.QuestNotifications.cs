using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private readonly List<QuestView> _questNotifications = new();
    private HudWindow? _questNotificationWindow;
    private int _questNotificationIndex;

    private void EnsureQuestNotificationWindow()
    {
        if (_questNotificationWindow != null) return;
        var layer = new CanvasLayer { Layer = 74 };
        AddChild(layer);
        _questNotificationWindow = new HudWindow("quest_available", "Quest available", new Vector2(40, 180), 420) { Visible = false };
        _questNotificationWindow.Closed += DismissQuestNotifications;
        layer.AddChild(_questNotificationWindow);
    }

    private void ShowQuestNotification(QuestView view)
    {
        EnsureQuestNotificationWindow();
        if (NewestFirst.Upsert(_questNotifications, view, q => q.QuestId)) _questNotificationIndex = 0;
        RefreshQuestNotification();
    }

    private void RefreshQuestNotification()
    {
        if (_questNotificationWindow == null) return;
        _questNotificationWindow.Visible = _questNotifications.Count > 0;
        if (_questNotifications.Count == 0) return;
        _questNotificationIndex = Mathf.Clamp(_questNotificationIndex, 0, _questNotifications.Count - 1);
        var view = _questNotifications[_questNotificationIndex];
        string self = Net.I?.LastEnter.Name ?? "";
        bool paged = _questNotifications.Count > 1;
        _questNotificationWindow.Title = paged
            ? $"Quests available ({_questNotificationIndex + 1} of {_questNotifications.Count})"
            : QuestMarkup.Plain(view.Title, self);
        var body = _questNotificationWindow.Body;
        foreach (var child in body.GetChildren()) { body.RemoveChild(child); child.QueueFree(); }
        if (paged) body.AddChild(QuestNotificationPager(view, self));
        body.AddChild(UiTheme.Text(QuestNotificationCaption(view), 12, UiTheme.Gold));
        body.AddChild(QuestParagraph(view.Dialogue, UiTheme.TextHi));
        for (var index = 0; index < view.Topics.Length; index++)
        {
            var choice = index;
            var button = new Button { Text = QuestMarkup.Plain(view.Topics[index], self), CustomMinimumSize = new Vector2(0, 36) };
            button.Pressed += () => AnswerQuestNotification(view.QuestId, choice);
            body.AddChild(button);
        }
        var close = new Button { Text = paged ? "Close all" : "Close", CustomMinimumSize = new Vector2(0, 36) };
        close.Pressed += DismissQuestNotifications;
        body.AddChild(close);
    }

    private Control QuestNotificationPager(QuestView view, string self)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var previous = UiTheme.IconButton("<", "Previous quest");
        previous.Pressed += () => StepQuestNotification(-1);
        row.AddChild(previous);
        var title = UiTheme.Text(QuestMarkup.Plain(view.Title, self), 14, UiTheme.TextHi);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(title);
        var next = UiTheme.IconButton(">", "Next quest");
        next.Pressed += () => StepQuestNotification(1);
        row.AddChild(next);
        return row;
    }

    private static string QuestNotificationCaption(QuestView view) => view.State switch
    {
        QuestViewState.Claimable => "Ready to turn in",
        QuestViewState.InProgress => "Quest started",
        QuestViewState.Completed => "Quest completed",
        _ => "Quest available",
    };

    private void StepQuestNotification(int step)
    {
        if (_questNotifications.Count == 0) return;
        _questNotificationIndex = (_questNotificationIndex + step + _questNotifications.Count) % _questNotifications.Count;
        RefreshQuestNotification();
    }

    private void AnswerQuestNotification(int questId, int choice)
    {
        _questNotifications.RemoveAll(q => q.QuestId == questId);
        RefreshQuestNotification();
        Net.I.SendQuestNotificationReply(questId, choice);
    }

    private void DismissQuestNotifications()
    {
        var ids = _questNotifications.Select(q => q.QuestId).ToArray();
        _questNotifications.Clear();
        _questNotificationIndex = 0;
        RefreshQuestNotification();
        foreach (var id in ids)
            Net.I.SendQuestNotificationReply(id, -1);
    }
}
