using Godot;
using static LibreKO.Features.Windows.ClanBoardUi;

namespace LibreKO.Features.Windows;

internal sealed class WishBoardFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly ClanBoardsSession boards;
    private readonly SelfSession self;
    private readonly IChatRequests chatRequests;
    private readonly IClanBoardsRequests boardsRequests;
    private readonly ClanBoardFeature clanBoard;
    private readonly MailComposeFeature mailCompose;

    public WishBoardFeature(IWorldContext context, ClanSession clan, ClanBoardsSession clanBoards, SelfSession self, IChatRequests chatRequests, IClanBoardsRequests clanBoardsRequests, ClanBoardFeature clanBoard, MailComposeFeature mailCompose)
    {
        this.context = context;
        this.clan = clan;
        boards = clanBoards;
        this.self = self;
        this.chatRequests = chatRequests;
        boardsRequests = clanBoardsRequests;
        this.clanBoard = clanBoard;
        this.mailCompose = mailCompose;
    }

    private const int WishMessageWidth = 320;
    private const int WishRequestListWidth = 400;
    private const string WishSettingsPath = "user://clanboards.cfg";
    private const string WishSettingsSection = "wish";
    private const string WishIgnoreKey = "ignore_notice";
    private static readonly Color WishOnlineColour = new(0.45f, 0.85f, 0.45f);

    private sealed class WishBoardRow
    {
        public PanelContainer Panel = null!;
        public Label Online = null!;
        public Label Name = null!;
        public Label Text = null!;
        public Button Details = null!;
    }

    internal HudWindow? WbWindow { get; private set; }
    private readonly WishBoardRow[] wbRows = new WishBoardRow[ClanBoards.RowsPerPage];
    private List<WishPost> wbPosts = new();
    private WishStatus wbStatus = new(WishBoardRole.Seeker, "", 0);
    private int wbPage = 1;
    private bool wbIgnoreNotice;
    private Label wbPageLabel = null!;
    private Label wbStatusText = null!;
    private Button wbPrev = null!;
    private Button wbNext = null!;
    private Button wbCancelRequest = null!;
    private Button wbRequestList = null!;
    private CheckButton wbIgnore = null!;
    private HBoxContainer wbSeekerRow = null!;

    internal HudWindow? WmWindow { get; private set; }
    private Label wmName = null!;
    private Label wmDetails = null!;
    private Label wmText = null!;
    private Label wmCounter = null!;
    private Label wmNote = null!;
    private LineEdit wmAge = null!;
    private TextEdit wmEdit = null!;
    private HBoxContainer wmButtons = null!;

    private HudWindow? wrWindow;
    private VBoxContainer wrRows = null!;

    public void Build()
    {
        clanBoard.WishBoardRequested += OpenWishBoard;
        clanBoard.WishRequestAccepted += CloseWishBoard;
        boards.ClanAdvertsEvent += OnAdverts;
        boards.WishPostsEvent += OnWishPosts;
        boards.WishStatusEvent += OnWishStatus;
        boards.WishTooSoonEvent += OnWishTooSoon;
        boards.WishRequestsEvent += OnWishRequests;
        boards.WishIntroResultEvent += OnWishIntroResult;
        boards.WishRequestResultEvent += OnWishRequestResult;
        boards.WishPromoteEvent += OnWishPromote;
        var cfg = new ConfigFile();
        if (cfg.Load(WishSettingsPath) == Error.Ok)
            wbIgnoreNotice = cfg.GetValue(WishSettingsSection, WishIgnoreKey, false).AsBool();
    }

    public void Dispose()
    {
        clanBoard.WishBoardRequested -= OpenWishBoard;
        clanBoard.WishRequestAccepted -= CloseWishBoard;
        boards.ClanAdvertsEvent -= OnAdverts;
        boards.WishPostsEvent -= OnWishPosts;
        boards.WishStatusEvent -= OnWishStatus;
        boards.WishTooSoonEvent -= OnWishTooSoon;
        boards.WishRequestsEvent -= OnWishRequests;
        boards.WishIntroResultEvent -= OnWishIntroResult;
        boards.WishRequestResultEvent -= OnWishRequestResult;
        boards.WishPromoteEvent -= OnWishPromote;
    }

    private void OnAdverts(List<ClanAdvert> adverts)
    {
        if (WbWindow != null) WbWindow.Visible = false;
    }

    private void OpenWishBoard()
    {
        clanBoard.HideBoard();
        wbPage = 1;
        boardsRequests.WishPage(wbPage, refresh: false);
    }

    private void CloseWishBoard()
    {
        if (WbWindow != null) WbWindow.Visible = false;
        if (WmWindow != null) WmWindow.Visible = false;
        if (wrWindow != null) wrWindow.Visible = false;
    }

    internal void OnWishStatus(WishStatus status)
    {
        wbStatus = status;
        wbPage = 1;
        wbPosts = new List<WishPost>();
        ShowWishBoard();
    }

    internal void OnWishPosts(List<WishPost> posts)
    {
        wbPosts = posts;
        ShowWishBoard();
    }

    private void ShowWishBoard()
    {
        WbWindow ??= BuildWishBoard();
        RefreshWishBoard();
        UiKit.ShowOnTop(WbWindow);
    }

    private void OnWishTooSoon() =>
        Notice.Show(context.Root, ItemData.Text(ClanBoards.TextRefreshTooSoon, "You can only refresh once every 30 seconds."));

    private HudWindow BuildWishBoard()
    {
        var window = new HudWindow("wish_board", "Bulletin Board for Join Request", new Vector2(250, 125), bodyMinWidth: Width) { Visible = false };
        window.Closed += CloseWishBoard;
        clanBoard.Layer.AddChild(window);
        var body = window.Body;

        var head = new HBoxContainer();
        body.AddChild(head);
        var intro = UiTheme.Text("You can request to join a clan as well as introduce yourself through this Bulletin Board. "
            + "Those who have authorities to accept new clan members can send the accept request.", 12, UiTheme.TextLo);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(intro);
        head.AddChild(BoardButton("Clan Promotional Bulletin Board", () => { CloseWishBoard(); clanBoard.Open(); }));

        var tools = new HBoxContainer();
        body.AddChild(tools);
        wbIgnore = new CheckButton { Text = "Rejection of Applicant", ButtonPressed = wbIgnoreNotice, FocusMode = Control.FocusModeEnum.None };
        wbIgnore.AddThemeFontSizeOverride("font_size", 12);
        wbIgnore.Toggled += on => SaveWishIgnore(on);
        tools.AddChild(wbIgnore);
        tools.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        tools.AddChild(BoardButton("Refresh", () => boardsRequests.WishPage(wbPage, refresh: true)));

        var captions = new HBoxContainer();
        body.AddChild(captions);
        captions.AddChild(new Control { CustomMinimumSize = new Vector2(MarkSize + 8, 0) });
        var idCaption = UiTheme.Text("ID", 12, UiTheme.Gold);
        idCaption.CustomMinimumSize = new Vector2(NameWidth, 0);
        captions.AddChild(idCaption);
        captions.AddChild(UiTheme.Text("Introduction", 12, UiTheme.Gold));

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        body.AddChild(rows);
        for (int i = 0; i < ClanBoards.RowsPerPage; i++)
        {
            wbRows[i] = BuildWishRow(i);
            rows.AddChild(wbRows[i].Panel);
        }

        var footer = new HBoxContainer();
        body.AddChild(footer);
        wbSeekerRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        wbSeekerRow.AddThemeConstantOverride("separation", 6);
        footer.AddChild(wbSeekerRow);
        wbSeekerRow.AddChild(BoardButton("Promote Self Introduction", boardsRequests.WishPromote));
        wbSeekerRow.AddChild(BoardButton("Write Self Introduction", OpenWishWrite));
        wbSeekerRow.AddChild(BoardButton("Delete Notes", () => { if (wrWindow != null) wrWindow.Visible = false; boardsRequests.WishIntroDelete(); }));
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        wbPrev = BoardButton("<", () => { if (wbPage > 1) boardsRequests.WishPage(--wbPage, refresh: false); });
        footer.AddChild(wbPrev);
        wbPageLabel = UiTheme.Text("1", 12, UiTheme.TextHi, HorizontalAlignment.Center);
        wbPageLabel.CustomMinimumSize = new Vector2(32, 0);
        footer.AddChild(wbPageLabel);
        wbNext = BoardButton(">", () => boardsRequests.WishPage(++wbPage, refresh: false));
        footer.AddChild(wbNext);

        body.AddChild(new HSeparator());
        var status = new HBoxContainer();
        status.AddThemeConstantOverride("separation", 8);
        body.AddChild(status);
        wbStatusText = UiTheme.Text("", 12, UiTheme.TextLo);
        wbStatusText.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        status.AddChild(wbStatusText);
        wbCancelRequest = BoardButton("Cancel Request", () => boardsRequests.WishRequestCancel(clan.MyClan.ClanId));
        status.AddChild(wbCancelRequest);
        wbRequestList = BoardButton("List of Requested Applicant", boardsRequests.WishRequestList);
        status.AddChild(wbRequestList);
        return window;
    }

    private WishBoardRow BuildWishRow(int index)
    {
        var row = new WishBoardRow
        {
            Panel = new PanelContainer { CustomMinimumSize = new Vector2(0, RowHeight) },
            Online = UiTheme.Text("●", 12, UiTheme.TextDim, HorizontalAlignment.Center),
            Name = UiTheme.Text("", 12, UiTheme.TextHi),
            Text = UiTheme.Text("", 12, UiTheme.TextLo),
        };
        row.Panel.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.Panel.AddChild(line);
        row.Online.CustomMinimumSize = new Vector2(MarkSize, 0);
        line.AddChild(row.Online);
        row.Name.CustomMinimumSize = new Vector2(NameWidth, 0);
        row.Name.ClipText = true;
        line.AddChild(row.Name);
        row.Text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.Text.ClipText = true;
        row.Text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        line.AddChild(row.Text);
        row.Details = BoardButton("Details", () => { if (index < wbPosts.Count) OpenWishView(wbPosts[index]); });
        line.AddChild(row.Details);
        return row;
    }

    private void RefreshWishBoard()
    {
        for (int i = 0; i < ClanBoards.RowsPerPage; i++)
        {
            var row = wbRows[i];
            bool used = i < wbPosts.Count;
            row.Online.Visible = row.Details.Visible = used;
            row.Online.AddThemeColorOverride("font_color", used && wbPosts[i].Online ? WishOnlineColour : UiTheme.TextDim);
            row.Name.Text = used ? wbPosts[i].Name : "";
            row.Text.Text = used ? wbPosts[i].Introduction : "";
        }
        wbPageLabel.Text = wbPage.ToString();
        wbPrev.Disabled = wbPage <= 1;
        wbNext.Disabled = !ClanBoards.CanPageForward(wbPosts.Count, ClanBoards.RowsPerPage);
        wbSeekerRow.Visible = wbStatus.Seeker;
        wbIgnore.Visible = !wbStatus.Seeker;
        wbCancelRequest.Visible = wbStatus.Recruiter && wbStatus.RequestedName.Length > 0;
        wbRequestList.Visible = wbStatus.Seeker && wbStatus.Received > 0;
        wbStatusText.Text = wbStatus switch
        {
            { Recruiter: true, RequestedName.Length: > 0 } => TextTemplate.Fill(ItemData.Text(ClanBoards.TextRequestSentTo, "A request has been sent to %s."), wbStatus.RequestedName),
            { Recruiter: true } => ItemData.Text(ClanBoards.TextNoRequestSent, "You have not sent the request yet."),
            { Member: true } => ItemData.Text(ClanBoards.TextMembersCannotRequest, "General Clan members cannot send membership request."),
            { Received: > 0 } => TextTemplate.Fill(ItemData.Text(ClanBoards.TextRequestsReceived, "Currently, %d requests are received."), wbStatus.Received),
            _ => ItemData.Text(ClanBoards.TextNoRequests, "You have not received requests."),
        };
    }

    private void SaveWishIgnore(bool on)
    {
        wbIgnoreNotice = on;
        var cfg = new ConfigFile();
        cfg.Load(WishSettingsPath);
        cfg.SetValue(WishSettingsSection, WishIgnoreKey, on);
        cfg.Save(WishSettingsPath);
    }

    private HudWindow BuildWishMessage()
    {
        var window = new HudWindow("wish_message", "Self Introduction", new Vector2(830, 140), bodyMinWidth: WishMessageWidth) { Visible = false };
        window.Closed += () => { window.Visible = false; if (WbWindow != null) UiKit.ShowOnTop(WbWindow); };
        clanBoard.Layer.AddChild(window);
        var body = window.Body;
        wmName = UiTheme.Text("", 15, UiTheme.GoldBright);
        body.AddChild(wmName);
        wmDetails = UiTheme.Text("", 12, UiTheme.TextLo);
        body.AddChild(wmDetails);
        var ageRow = new HBoxContainer();
        body.AddChild(ageRow);
        ageRow.AddChild(UiTheme.Text("Age", 12, UiTheme.TextLo));
        wmAge = new LineEdit { MaxLength = 2, CustomMinimumSize = new Vector2(48, 0) };
        ageRow.AddChild(wmAge);
        wmText = UiTheme.Text("", 13, UiTheme.TextHi);
        wmText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        wmText.CustomMinimumSize = new Vector2(WishMessageWidth, 0);
        body.AddChild(wmText);
        wmEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(WishMessageWidth, MessageEditHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        wmEdit.TextChanged += () => wmCounter.Text = $"{wmEdit.Text.Length}/{ClanBoards.MaxIntroBytes}";
        body.AddChild(wmEdit);
        wmCounter = UiTheme.Text("", 11, UiTheme.TextDim, HorizontalAlignment.Right);
        body.AddChild(wmCounter);
        wmNote = UiTheme.Text("Messages created are automatically deleted after 7 days.", 11, UiTheme.TextDim);
        body.AddChild(wmNote);
        wmButtons = new HBoxContainer();
        wmButtons.AddThemeConstantOverride("separation", 6);
        body.AddChild(wmButtons);
        return window;
    }

    private void ResetWishButtons()
    {
        foreach (var child in wmButtons.GetChildren()) { wmButtons.RemoveChild(child); child.QueueFree(); }
    }

    private void OpenWishWrite()
    {
        string selfName = self.LastEnter.Name;
        if (wbPosts.Exists(post => string.Equals(post.Name, selfName, System.StringComparison.OrdinalIgnoreCase)))
        {
            Notice.Show(context.Root, ItemData.Text(ClanBoards.TextAlreadyRegistered, "Already Registered"));
            return;
        }
        WmWindow ??= BuildWishMessage();
        wmName.Text = selfName;
        wmDetails.Text = $"Lv {self.Sheet.Level}  ·  {CharacterClassCatalog.DisplayName(self.Class)}";
        wmAge.GetParent<Control>().Visible = true;
        wmAge.Text = "";
        wmText.Visible = false;
        wmEdit.Visible = wmCounter.Visible = wmNote.Visible = true;
        wmEdit.Text = "";
        wmCounter.Text = $"0/{ClanBoards.MaxIntroBytes}";
        ResetWishButtons();
        wmButtons.AddChild(BoardButton("Confirmed", SendWishIntro));
        UiKit.ShowOnTop(WmWindow);
    }

    private void SendWishIntro()
    {
        if (!ClanBoards.AgeValid(wmAge.Text))
        {
            Notice.Show(context.Root, ItemData.Text(ClanBoards.TextAgeWrong, "Please enter the age correctly."));
            return;
        }
        string text = wmEdit.Text.Trim();
        if (text.Length == 0 || text.Length > ClanBoards.MaxIntroBytes)
        {
            Notice.Show(context.Root, ItemData.Text(ClanBoards.TextOverLimit, "Length of message is over limit"));
            return;
        }
        if (!wbStatus.Seeker) return;
        boardsRequests.WishIntroRegister(int.Parse(wmAge.Text), text);
    }

    internal void OpenWishView(WishPost post)
    {
        WmWindow ??= BuildWishMessage();
        wmName.Text = post.Name;
        wmDetails.Text = $"Lv {post.Level}  ·  {CharacterClassCatalog.DisplayName(post.Class)}  ·  Age {post.Age}  ·  Recent Registration Date {post.Registered}";
        wmAge.GetParent<Control>().Visible = false;
        wmText.Visible = true;
        wmText.Text = post.Introduction;
        wmEdit.Visible = wmCounter.Visible = wmNote.Visible = false;
        ResetWishButtons();
        if (wbStatus.Recruiter)
        {
            wmButtons.AddChild(BoardButton("Request to Join", () => RequestWishJoin(post)));
            wmButtons.AddChild(BoardButton("Write a Note", () => mailCompose.OpenComposeTo(post.Name)));
            wmButtons.AddChild(BoardButton("Request for Consultation", () => chatRequests.PlayerConsultation(post.Name)));
            wmButtons.AddChild(BoardButton("OK", () => WmWindow.Visible = false));
        }
        else
        {
            wmButtons.AddChild(BoardButton("Close", () => WmWindow.Visible = false));
        }
        UiKit.ShowOnTop(WmWindow);
    }

    private void RequestWishJoin(WishPost post)
    {
        if (post.TargetId != ClanBoards.NoTarget)
        {
            boardsRequests.WishOnlineRequest(post.TargetId, post.Name);
            WmWindow!.Visible = false;
            return;
        }
        wmText.Visible = false;
        wmEdit.Visible = wmCounter.Visible = true;
        wmEdit.Text = "";
        ResetWishButtons();
        wmButtons.AddChild(BoardButton("Confirmed", () => boardsRequests.WishOfflineRequest(clan.MyClan.ClanId, post.Name, wmEdit.Text.Trim())));
    }

    private void OnWishIntroResult(ClanBoardAction action, int code)
    {
        if (ClanBoards.IntroResultText(action, code) is var text and > 0) Notice.Show(context.Root, ItemData.Text(text, ""));
        if (action == ClanBoardAction.Delete && code == 1 && WmWindow != null) WmWindow.Visible = false;
        if (action == ClanBoardAction.Register && code == 1 && WmWindow != null) WmWindow.Visible = false;
        boardsRequests.WishPage(wbPage, refresh: false);
    }

    private void OnWishRequestResult(ClanBoardAction action, int code)
    {
        if (ClanBoards.RequestResultText(action, code) is var text and > 0) Notice.Show(context.Root, ItemData.Text(text, ""));
        if (action == ClanBoardAction.Register && code == 1 && WmWindow != null) WmWindow.Visible = false;
        if (action == ClanBoardAction.Delete && wrWindow is { Visible: true } && wbStatus.Seeker) boardsRequests.WishRequestList();
        boardsRequests.WishPage(wbPage, refresh: false);
    }

    private void OnWishPromote(int code, string name)
    {
        if (name.Length > 0)
        {
            if (wbIgnoreNotice && clan.MyClan.InClan) return;
            context.Log.Notice(TextTemplate.Fill(ItemData.Text(ClanBoards.TextSeekingClan,
                "%s is seeking a clan. Please check the Bulletin Board for Join Request."), name));
            return;
        }
        if (ClanBoards.WishPromoteText(code) is var text and > 0) Notice.Show(context.Root, ItemData.Text(text, ""));
    }

    private void OnWishRequests(List<WishRequest> wishRequests)
    {
        if (WmWindow != null) WmWindow.Visible = false;
        wrWindow ??= BuildWishRequestList();
        foreach (var child in wrRows.GetChildren()) { wrRows.RemoveChild(child); child.QueueFree(); }
        foreach (var request in wishRequests)
        {
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            panel.AddChild(line);
            var leader = UiTheme.Text(request.Leader, 12, UiTheme.TextHi);
            leader.CustomMinimumSize = new Vector2(NameWidth - 30, 0);
            line.AddChild(leader);
            var clanLabel = UiTheme.Text(request.ClanName, 12, UiTheme.TextLo);
            clanLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(clanLabel);
            var captured = request;
            line.AddChild(BoardButton("Message", () => clanBoard.OpenMessage(ClanBoardFeature.MessageMode.ViewWishRequest, null, request: captured)));
            line.AddChild(BoardButton("Delete", () => boardsRequests.WishRequestCancel(captured.ClanId)));
            wrRows.AddChild(panel);
        }
        if (wishRequests.Count == 0) wrRows.AddChild(UiTheme.Text("You have not received requests.", 12, UiTheme.TextLo));
        UiKit.ShowOnTop(wrWindow);
    }

    private HudWindow BuildWishRequestList()
    {
        var window = new HudWindow("wish_requests", "List of Requested Applicant", new Vector2(270, 150), bodyMinWidth: WishRequestListWidth) { Visible = false };
        window.Closed += () => { window.Visible = false; if (WbWindow != null) UiKit.ShowOnTop(WbWindow); };
        clanBoard.Layer.AddChild(window);
        window.Body.AddChild(UiTheme.Text("There can be up to 9 applicants.", 12, UiTheme.TextLo));
        wrRows = new VBoxContainer();
        wrRows.AddThemeConstantOverride("separation", 2);
        window.Body.AddChild(wrRows);
        return window;
    }
}
