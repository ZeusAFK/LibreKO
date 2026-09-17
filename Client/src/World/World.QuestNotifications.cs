using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private readonly List<QuestView> _questNotifications = new();
    private HudWindow? _questNotificationWindow;

    private void EnsureQuestNotificationWindow()
    {
        if (_questNotificationWindow != null) return;
        var layer = new CanvasLayer { Layer = 74 };
        AddChild(layer);
        _questNotificationWindow = new HudWindow("quest_available", "Quest available", new Vector2(40, 180), 420) { Visible = false };
        _questNotificationWindow.Closed += () => AnswerQuestNotification(-1);
        layer.AddChild(_questNotificationWindow);
    }

    private void ShowQuestNotification(QuestView view)
    {
        EnsureQuestNotificationWindow();
        var index = _questNotifications.FindIndex(q => q.QuestId == view.QuestId);
        if (index < 0) _questNotifications.Add(view);
        else _questNotifications[index] = view;
        RefreshQuestNotification();
    }

    private void RefreshQuestNotification()
    {
        if (_questNotificationWindow == null) return;
        _questNotificationWindow.Visible = _questNotifications.Count > 0;
        if (_questNotifications.Count == 0) return;
        var view = _questNotifications[0];
        _questNotificationWindow.Title = QuestMarkup.Plain(view.Title, Net.I?.LastEnter.Name ?? "");
        var body = _questNotificationWindow.Body;
        foreach (var child in body.GetChildren()) { body.RemoveChild(child); child.QueueFree(); }
        body.AddChild(UiTheme.Text(
            view.State == QuestViewState.Claimable ? "Ready to turn in"
                : view.State == QuestViewState.InProgress ? "Quest started"
                : view.State == QuestViewState.Completed ? "Quest completed" : "Quest available",
            12, UiTheme.Gold));
        body.AddChild(QuestParagraph(view.Dialogue, UiTheme.TextHi));
        for (var index = 0; index < view.Topics.Length; index++)
        {
            var choice = index;
            var button = new Button { Text = QuestMarkup.Plain(view.Topics[index], Net.I?.LastEnter.Name ?? ""), CustomMinimumSize = new Vector2(0, 36) };
            button.Pressed += () => AnswerQuestNotification(choice);
            body.AddChild(button);
        }
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(0, 36) };
        close.Pressed += () => AnswerQuestNotification(-1);
        body.AddChild(close);
    }

    private void AnswerQuestNotification(int choice)
    {
        if (_questNotifications.Count == 0) return;
        var id = _questNotifications[0].QuestId;
        _questNotifications.RemoveAt(0);
        RefreshQuestNotification();
        Net.I.SendQuestNotificationReply(id, choice);
    }
}
