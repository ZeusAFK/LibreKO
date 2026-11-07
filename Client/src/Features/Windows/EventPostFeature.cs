using Godot;

namespace LibreKO.Features.Windows;

internal sealed class EventPostFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly EventPostSession eventPost;
    private readonly IEventPostRequests requests;

    public EventPostFeature(IWorldContext context, EventPostSession eventPost, IEventPostRequests requests)
    {
        this.context = context;
        this.eventPost = eventPost;
        this.requests = requests;
    }

    private const string BoardWindowId = "eventpost";
    private const string HistoryWindowId = "eventposthistory";
    private const float BoardWidth = 620f;
    private const float CardWidth = 196f;
    private const float HistoryWidth = 340f;
    private const int BodySize = 12;

    private CanvasLayer layer = null!;
    internal HudWindow BoardWindow { get; private set; } = null!;
    internal HudWindow HistoryWindow { get; private set; } = null!;
    private Label howTo = null!;
    private Label note = null!;
    private LineEdit phrase = null!;
    private Label today = null!;
    private Label total = null!;
    private HBoxContainer cards = null!;
    private ServicePager postPager = null!;
    private VBoxContainer historyRows = null!;
    private ServicePager historyPager = null!;
    private EventPostEvent shownEvent;

    internal IClosableWindow BoardPanel => new ClosablePanel(() => BoardWindow.Visible, Close);
    internal IClosableWindow HistoryPanel => new ClosablePanel(() => HistoryWindow.Visible, CloseHistory);

    public void Build()
    {
        layer = new CanvasLayer { Layer = UiLayers.Windows };
        context.Root.AddChild(layer);
        BuildBoard();
        BuildHistory();
        eventPost.BoardEvent += OnBoard;
        eventPost.RegisteredEvent += OnRegistered;
        eventPost.HistoryEvent += OnHistory;
    }

    public void Dispose()
    {
        eventPost.BoardEvent -= OnBoard;
        eventPost.RegisteredEvent -= OnRegistered;
        eventPost.HistoryEvent -= OnHistory;
    }

    private void BuildBoard()
    {
        BoardWindow = new HudWindow(BoardWindowId, "") { Visible = false };
        BoardWindow.Closed += Close;
        BoardWindow.Body.CustomMinimumSize = new Vector2(BoardWidth, 0);
        BoardWindow.Body.AddThemeConstantOverride("separation", 8);
        layer.AddChild(BoardWindow);

        var guide = new HBoxContainer();
        guide.AddThemeConstantOverride("separation", 8);
        howTo = GuideColumn(guide);
        note = GuideColumn(guide);
        BoardWindow.Body.AddChild(guide);

        var entry = new HBoxContainer();
        entry.AddThemeConstantOverride("separation", 6);
        phrase = new LineEdit { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        phrase.AddThemeFontSizeOverride("font_size", 13);
        phrase.TextSubmitted += _ => Post();
        entry.AddChild(phrase);
        var post = UiTheme.ActionButton("Post", "Register today's post");
        post.Pressed += Post;
        entry.AddChild(post);
        BoardWindow.Body.AddChild(entry);

        today = UiTheme.Text("", BodySize, UiTheme.TextLo);
        total = UiTheme.Text("", BodySize, UiTheme.TextLo);
        BoardWindow.Body.AddChild(today);
        BoardWindow.Body.AddChild(total);

        cards = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        cards.AddThemeConstantOverride("separation", 8);
        BoardWindow.Body.AddChild(cards);

        var footer = new HBoxContainer();
        var history = UiTheme.SmallButton(ItemData.Text(EventPostText.History.Text, EventPostText.History.Fallback), "Your rewards");
        history.Pressed += ToggleHistory;
        footer.AddChild(history);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        postPager = new ServicePager();
        postPager.PageChanged += _ => ShowPosts();
        footer.AddChild(postPager);
        BoardWindow.Body.AddChild(footer);
    }

    private static Label GuideColumn(HBoxContainer row)
    {
        var section = UiTheme.Section();
        section.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var label = UiTheme.Text("", BodySize, UiTheme.TextLo);
        UiKit.Wrap(label);
        section.AddChild(label);
        row.AddChild(section);
        return label;
    }

    private void BuildHistory()
    {
        HistoryWindow = new HudWindow(HistoryWindowId, ItemData.Text(EventPostText.History.Text, EventPostText.History.Fallback)) { Visible = false };
        HistoryWindow.Closed += CloseHistory;
        HistoryWindow.Body.CustomMinimumSize = new Vector2(HistoryWidth, 0);
        HistoryWindow.Body.AddThemeConstantOverride("separation", 6);
        layer.AddChild(HistoryWindow);

        historyRows = new VBoxContainer();
        historyRows.AddThemeConstantOverride("separation", 2);
        HistoryWindow.Body.AddChild(historyRows);
        historyPager = new ServicePager { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        historyPager.PageChanged += _ => ShowHistory();
        HistoryWindow.Body.AddChild(historyPager);
    }

    internal void Open(EventPostEvent eventId)
    {
        shownEvent = eventId;
        var (titleText, titleFallback) = EventPostText.Title(eventId);
        BoardWindow.Title = ItemData.Text(titleText, titleFallback);
        var (howToText, howToFallback) = EventPostText.HowTo(eventId);
        howTo.Text = ItemData.Text(howToText, howToFallback);
        var (noteText, noteFallback) = EventPostText.Note(eventId);
        note.Text = ItemData.Text(noteText, noteFallback);
        phrase.Text = "";
        phrase.PlaceholderText = EventPostText.Phrase(eventId);
        today.Text = "";
        total.Text = "";
        UiKit.ClearChildren(cards);
        postPager.Set(0, 1);
        BoardWindow.Visible = true;
        UiKit.ShowOnTop(BoardWindow);
        requests.Board(eventId);
    }

    internal void Close()
    {
        BoardWindow.Visible = false;
        CloseHistory();
    }

    internal void CloseHistory() => HistoryWindow.Visible = false;

    private void ToggleHistory()
    {
        if (HistoryWindow.Visible) { CloseHistory(); return; }
        UiKit.ClearChildren(historyRows);
        historyPager.Set(0, 1);
        HistoryWindow.Visible = true;
        UiKit.ShowOnTop(HistoryWindow);
        requests.History();
    }

    private void Post()
    {
        string text = phrase.Text.Trim();
        if (text.Length == 0) return;
        requests.Post(shownEvent, text);
    }

    private void OnBoard(EventPostReply reply)
    {
        if (!BoardWindow.Visible || reply.Event != shownEvent) return;
        if (reply.Board == null)
        {
            context.Log.Notice(ItemData.Text(EventPostText.UpdateFailed.Text, EventPostText.UpdateFailed.Fallback));
            return;
        }
        ShowBoard(reply.Board);
    }

    private void OnRegistered(EventPostReply reply)
    {
        if (EventPostText.Refusal(reply.Result) is { } refusal)
        {
            context.Log.Notice(ItemData.Text(refusal.Text, refusal.Fallback));
            return;
        }
        phrase.Text = "";
        if (BoardWindow.Visible && reply.Event == shownEvent && reply.Board != null) ShowBoard(reply.Board);
        if (HistoryWindow.Visible) requests.History();
    }

    private void ShowBoard(EventPostBoard board)
    {
        today.Text = $"{ItemData.Text(EventPostText.Today.Text, EventPostText.Today.Fallback)} : {board.Today}";
        total.Text = $"{ItemData.Text(EventPostText.Total.Text, EventPostText.Total.Fallback)} "
                     + $"({EventPostText.Period(board.PeriodStart, board.PeriodEnd)}) : {board.InPeriod}";
        postPager.Set(0, Paging.Count(board.Posts.Count, EventPostText.PostsPerPage));
        ShowPosts();
    }

    private void ShowPosts()
    {
        UiKit.ClearChildren(cards);
        if (eventPost.Board?.Board is not { } board) return;
        foreach (var post in board.Posts.Skip(postPager.Page * EventPostText.PostsPerPage).Take(EventPostText.PostsPerPage))
            cards.AddChild(Card(post));
    }

    private static Control Card(EventPostEntry post)
    {
        var card = UiTheme.Section();
        card.CustomMinimumSize = new Vector2(CardWidth, 0);
        var lines = new VBoxContainer();
        lines.AddThemeConstantOverride("separation", 3);
        lines.AddChild(UiTheme.Text($"Server : {post.Server}", BodySize, UiTheme.TextLo));
        lines.AddChild(UiTheme.Text($"Character : {post.Name}", BodySize, UiTheme.TextHi));
        lines.AddChild(UiTheme.Text(post.Text, 13, UiTheme.GoldBright));
        lines.AddChild(UiTheme.Text(EventPostText.Day(post.Time), 11, UiTheme.TextDim));
        card.AddChild(lines);
        return card;
    }

    private void OnHistory(IReadOnlyList<EventPostHistoryEntry> history)
    {
        if (!HistoryWindow.Visible) return;
        historyPager.Set(0, Paging.Count(history.Count, EventPostText.HistoryPerPage));
        ShowHistory();
    }

    private void ShowHistory()
    {
        UiKit.ClearChildren(historyRows);
        foreach (var entry in eventPost.History.Skip(historyPager.Page * EventPostText.HistoryPerPage).Take(EventPostText.HistoryPerPage))
        {
            var row = new HBoxContainer();
            var (titleText, titleFallback) = EventPostText.Title(entry.Event);
            var name = UiTheme.Text(ItemData.Text(titleText, titleFallback), BodySize, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(name);
            row.AddChild(UiTheme.Text(EventPostText.Moment(entry.Time), BodySize, UiTheme.TextLo));
            historyRows.AddChild(row);
        }
    }
}
