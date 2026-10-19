using Godot;

namespace LibreKO.Features.Hud;

internal sealed class QuestNotificationsFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly SelfSession self;
    private readonly INpcQuestRequests npcRequests;

    public QuestNotificationsFeature(IWorldContext context, SelfSession self, INpcQuestRequests npcQuestRequests)
    {
        this.context = context;
        this.self = self;
        npcRequests = npcQuestRequests;
    }

    public void Build()
    {
    }

    bool IEscapable.CloseOnEscape()
    {
        if (QuestNotifications.Count > 0) Dismiss();
        else return false;
        return true;
    }

    public void Dispose()
    {
    }

    internal List<QuestView> QuestNotifications { get; } = new();
    internal HudWindow? NotificationWindow { get; private set; }
    private int notificationIndex;

    private void EnsureNotificationWindow()
    {
        if (NotificationWindow != null) return;
        var layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        NotificationWindow = new HudWindow("quest_available", "Quest available", new Vector2(40, 180), 420) { Visible = false };
        NotificationWindow.Closed += Dismiss;
        layer.AddChild(NotificationWindow);
    }

    internal void ShowNotification(QuestView view)
    {
        EnsureNotificationWindow();
        if (NewestFirst.Upsert(QuestNotifications, view, q => q.QuestId)) notificationIndex = 0;
        RefreshNotification();
    }

    internal void RefreshNotification()
    {
        if (NotificationWindow == null) return;
        NotificationWindow.Visible = QuestNotifications.Count > 0;
        if (QuestNotifications.Count == 0) return;
        notificationIndex = Mathf.Clamp(notificationIndex, 0, QuestNotifications.Count - 1);
        var view = QuestNotifications[notificationIndex];
        string selfName = self.LastEnter.Name ?? "";
        bool paged = QuestNotifications.Count > 1;
        NotificationWindow.Title = paged
            ? $"Quests available ({notificationIndex + 1} of {QuestNotifications.Count})"
            : QuestMarkup.Plain(view.Title, selfName);
        var body = NotificationWindow.Body;
        foreach (var child in body.GetChildren()) { body.RemoveChild(child); child.QueueFree(); }
        if (paged) body.AddChild(NotificationPager(view, selfName));
        body.AddChild(UiTheme.Text(NotificationCaption(view), 12, UiTheme.Gold));
        body.AddChild(QuestRows.Paragraph(view.Dialogue, UiTheme.TextHi, self.LastEnter.Name));
        for (var index = 0; index < view.Topics.Length; index++)
        {
            var choice = index;
            var button = new Button { Text = QuestMarkup.Plain(view.Topics[index], selfName), CustomMinimumSize = new Vector2(0, 36) };
            button.Pressed += () => AnswerNotification(view.QuestId, choice);
            body.AddChild(button);
        }
        var close = new Button { Text = paged ? "Close all" : "Close", CustomMinimumSize = new Vector2(0, 36) };
        close.Pressed += Dismiss;
        body.AddChild(close);
    }

    private Control NotificationPager(QuestView view, string selfName)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var previous = UiTheme.IconButton("<", "Previous quest");
        previous.Pressed += () => StepNotification(-1);
        row.AddChild(previous);
        var title = UiTheme.Text(QuestMarkup.Plain(view.Title, selfName), 14, UiTheme.TextHi);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(title);
        var next = UiTheme.IconButton(">", "Next quest");
        next.Pressed += () => StepNotification(1);
        row.AddChild(next);
        return row;
    }

    private static string NotificationCaption(QuestView view) => view.State switch
    {
        QuestViewState.Claimable => "Ready to turn in",
        QuestViewState.InProgress => "Quest started",
        QuestViewState.Completed => "Quest completed",
        _ => "Quest available",
    };

    private void StepNotification(int step)
    {
        if (QuestNotifications.Count == 0) return;
        notificationIndex = (notificationIndex + step + QuestNotifications.Count) % QuestNotifications.Count;
        RefreshNotification();
    }

    private void AnswerNotification(int questId, int choice)
    {
        Forget(questId);
        npcRequests.QuestNotificationReply(questId, choice);
    }

    internal void Forget(int questId)
    {
        QuestNotifications.RemoveAll(q => q.QuestId == questId);
        RefreshNotification();
    }

    internal void Dismiss()
    {
        var ids = QuestNotifications.Select(q => q.QuestId).ToArray();
        QuestNotifications.Clear();
        notificationIndex = 0;
        RefreshNotification();
        foreach (var id in ids)
            npcRequests.QuestNotificationReply(id, -1);
    }
}
