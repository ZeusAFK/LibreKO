using Godot;
using static LibreKO.Features.Windows.ClanBoardUi;

namespace LibreKO.Features.Windows;

internal sealed partial class ClanBoardFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ChatSession chat;
    private readonly ClanSession clan;
    private readonly ClanBoardsSession boards;
    private readonly SelfSession self;
    private readonly IChatRequests chatRequests;
    private readonly IClanRequests requests;
    private readonly IClanBoardsRequests boardsRequests;
    private readonly ClanMarksFeature marks;
    private readonly WhispersFeature whispers;

    public ClanBoardFeature(IWorldContext context, ChatSession chat, ClanSession clan, ClanBoardsSession clanBoards, SelfSession self, IChatRequests chatRequests, IClanRequests clanRequests, IClanBoardsRequests clanBoardsRequests, ClanMarksFeature clanMarks, WhispersFeature whispers)
    {
        this.context = context;
        this.chat = chat;
        this.clan = clan;
        boards = clanBoards;
        this.self = self;
        this.chatRequests = chatRequests;
        requests = clanRequests;
        boardsRequests = clanBoardsRequests;
        marks = clanMarks;
        this.whispers = whispers;
    }

    private const int MessageWidth = 340;
    private const int JoinListWidth = 420;

    internal enum MessageMode { WriteAdvert, ViewAdvert, ComposeApply, ViewApplicant, ViewWishRequest }

    private sealed class ClanBoardRow
    {
        public PanelContainer Panel = null!;
        public TextureRect Mark = null!;
        public Label Name = null!;
        public Label Text = null!;
        public Button Details = null!;
    }

    private CanvasLayer cbLayer = null!;
    internal HudWindow? CbWindow { get; private set; }
    private readonly ClanBoardRow[] cbRows = new ClanBoardRow[ClanBoards.RowsPerPage];
    private List<ClanAdvert> cbAdverts = new();
    private ClanBoardStatus cbStatus = new(ClanBoardState.Clanless, new List<ClanApplicant>(), 0, 0, "");
    private int cbPage = 1;
    private Label cbPageLabel = null!;
    private Button cbPrev = null!;
    private Button cbNext = null!;
    private HBoxContainer cbStaffRow = null!;
    private HBoxContainer cbStatusRow = null!;

    internal HudWindow? CmWindow { get; private set; }
    private MessageMode cmMode;
    private int cmId;
    private string cmName = "";
    private TextureRect cmMark = null!;
    private Label cmClanName = null!;
    private Label cmClassLine = null!;
    private Label cmLeader = null!;
    private Label cmMessage = null!;
    private Label cmCounter = null!;
    private Label cmWarning = null!;
    private TextEdit cmEdit = null!;
    private HBoxContainer cmButtons = null!;

    private HudWindow? cjWindow;
    private VBoxContainer cjRows = null!;
    private int cjPage;
    private Label cjPageLabel = null!;

    public void Build()
    {
        cbLayer = new CanvasLayer { Layer = UiLayers.Boards };
        context.Root.AddChild(cbLayer);
        boards.ClanBoardStatusEvent += OnStatus;
        boards.ClanAdvertsEvent += OnAdverts;
        boards.ClanApplyResultEvent += OnApplyResult;
        boards.ClanAdvertResultEvent += OnAdvertResult;
        boards.ClanPromoteEvent += OnPromote;
        chat.ClanConsultationEvent += OnConsultation;
    }

    public void Dispose()
    {
        boards.ClanBoardStatusEvent -= OnStatus;
        boards.ClanAdvertsEvent -= OnAdverts;
        boards.ClanApplyResultEvent -= OnApplyResult;
        boards.ClanAdvertResultEvent -= OnAdvertResult;
        boards.ClanPromoteEvent -= OnPromote;
        chat.ClanConsultationEvent -= OnConsultation;
    }

    internal event Action? WishBoardRequested;

    internal event Action? WishRequestAccepted;

    internal CanvasLayer Layer => cbLayer;

    internal void HideBoard()
    {
        if (CbWindow != null) CbWindow.Visible = false;
    }

    internal void Open()
    {
        if (CbWindow is { Visible: true })
        {
            Close();
            return;
        }
        cbPage = 1;
        boardsRequests.ClanBoardOpen(cbPage);
    }

    private void Close()
    {
        if (CbWindow != null) CbWindow.Visible = false;
        if (CmWindow != null) CmWindow.Visible = false;
        if (cjWindow != null) cjWindow.Visible = false;
    }

    internal void OnStatus(ClanBoardStatus status) => cbStatus = status;

    internal void OnAdverts(List<ClanAdvert> adverts)
    {
        cbAdverts = adverts;
        CbWindow ??= BuildClanBoard();
        cbStatus = cbStatus with { State = clan.MyClan.InClan ? ((clan.MyClan.IsChief || clan.MyClan.IsViceChief) ? ClanBoardState.Staff : ClanBoardState.Member) : ClanBoardState.Clanless };
        Refresh();
        UiKit.ShowOnTop(CbWindow);
    }

    private HudWindow BuildClanBoard()
    {
        var window = new HudWindow("clan_board", "Clan Recruitment Announcement", new Vector2(240, 120), bodyMinWidth: Width) { Visible = false };
        window.Closed += Close;
        cbLayer.AddChild(window);
        var body = window.Body;

        var head = new HBoxContainer();
        body.AddChild(head);
        var intro = UiTheme.Text("You can recruit clan members as well as request to join a clan through this Bulletin Board.", 12, UiTheme.TextLo);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(intro);
        head.AddChild(BoardButton("Bulletin Board for Join Request", () => WishBoardRequested?.Invoke()));

        var captions = new HBoxContainer();
        body.AddChild(captions);
        captions.AddChild(new Control { CustomMinimumSize = new Vector2(MarkSize + 8, 0) });
        var clanCaption = UiTheme.Text("Clan", 12, UiTheme.Gold);
        clanCaption.CustomMinimumSize = new Vector2(NameWidth, 0);
        captions.AddChild(clanCaption);
        var textCaption = UiTheme.Text("Clan Introduction", 12, UiTheme.Gold);
        textCaption.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        captions.AddChild(textCaption);
        captions.AddChild(BoardButton("Refresh", () => { cbPage = 1; boardsRequests.ClanBoardOpen(cbPage); }));

        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 2);
        body.AddChild(rows);
        for (int i = 0; i < ClanBoards.RowsPerPage; i++)
        {
            cbRows[i] = BuildRow(i);
            rows.AddChild(cbRows[i].Panel);
        }

        var footer = new HBoxContainer();
        body.AddChild(footer);
        cbStaffRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cbStaffRow.AddThemeConstantOverride("separation", 6);
        footer.AddChild(cbStaffRow);
        cbStaffRow.AddChild(BoardButton("Promote Clan", requests.Promote));
        cbStaffRow.AddChild(BoardButton("Write Promotional Notes", () => OpenMessage(MessageMode.WriteAdvert, null)));
        cbStaffRow.AddChild(BoardButton("Delete Notes", boardsRequests.ClanAdvertDelete));
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        cbPrev = BoardButton("<", () => { if (cbPage > 1) boardsRequests.ClanBoardPage(--cbPage); });
        footer.AddChild(cbPrev);
        cbPageLabel = UiTheme.Text("1", 12, UiTheme.TextHi, HorizontalAlignment.Center);
        cbPageLabel.CustomMinimumSize = new Vector2(32, 0);
        footer.AddChild(cbPageLabel);
        cbNext = BoardButton(">", () => boardsRequests.ClanBoardPage(++cbPage));
        footer.AddChild(cbNext);

        body.AddChild(new HSeparator());
        cbStatusRow = new HBoxContainer();
        cbStatusRow.AddThemeConstantOverride("separation", 8);
        body.AddChild(cbStatusRow);
        return window;
    }

    private ClanBoardRow BuildRow(int index)
    {
        var row = new ClanBoardRow
        {
            Panel = new PanelContainer { CustomMinimumSize = new Vector2(0, RowHeight) },
            Mark = new TextureRect
            {
                CustomMinimumSize = new Vector2(MarkSize, MarkSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            },
            Name = UiTheme.Text("", 12, UiTheme.TextHi),
            Text = UiTheme.Text("", 12, UiTheme.TextLo),
        };
        row.Panel.AddThemeStyleboxOverride("panel", UiTheme.Row());
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        row.Panel.AddChild(line);
        line.AddChild(row.Mark);
        row.Name.CustomMinimumSize = new Vector2(NameWidth, 0);
        row.Name.ClipText = true;
        line.AddChild(row.Name);
        row.Text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.Text.ClipText = true;
        row.Text.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        line.AddChild(row.Text);
        row.Details = BoardButton("Details", () =>
        {
            if (index < cbAdverts.Count) OpenMessage(MessageMode.ViewAdvert, cbAdverts[index]);
        });
        line.AddChild(row.Details);
        return row;
    }

    private void Refresh()
    {
        for (int i = 0; i < ClanBoards.RowsPerPage; i++)
        {
            var row = cbRows[i];
            bool used = i < cbAdverts.Count;
            row.Mark.Visible = row.Details.Visible = used;
            row.Name.Text = used ? cbAdverts[i].ClanName : "";
            row.Text.Text = used ? cbAdverts[i].Text : "";
            row.Mark.Texture = used ? marks.Mark(cbAdverts[i].ClanId, cbAdverts[i].MarkVersion) : null;
        }
        cbPageLabel.Text = cbPage.ToString();
        cbPrev.Disabled = cbPage <= 1;
        cbNext.Disabled = !ClanBoards.CanPageForward(cbAdverts.Count, ClanBoards.RowsPerPage);
        cbStaffRow.Visible = cbStatus.Staff;

        foreach (var child in cbStatusRow.GetChildren()) { cbStatusRow.RemoveChild(child); child.QueueFree(); }
        if (cbStatus.Staff)
        {
            int count = cbStatus.Applicants.Count;
            cbStatusRow.AddChild(UiTheme.Text(count > 0 ? $"Clan applicants: {count}" : "No one has applied to your clan yet.", 12, UiTheme.TextLo));
            if (count > 0) cbStatusRow.AddChild(BoardButton("List of Applicants", OpenJoinList));
        }
        else if (cbStatus.Applied)
        {
            cbStatusRow.AddChild(new TextureRect
            {
                Texture = marks.Mark(cbStatus.AppliedClanId, cbStatus.AppliedMarkVersion),
                CustomMinimumSize = new Vector2(MarkSize, MarkSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            });
            var applied = UiTheme.Text($"You applied to {cbStatus.AppliedClanName}.", 12, UiTheme.TextHi);
            applied.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            cbStatusRow.AddChild(applied);
            cbStatusRow.AddChild(BoardButton("Cancel Request", () => boardsRequests.ClanApplyCancel(self.LastEnter.Name)));
        }
        else
        {
            cbStatusRow.AddChild(UiTheme.Text(clan.MyClan.InClan ? "You are in a clan." : "You have not applied to a clan.", 12, UiTheme.TextLo));
        }
    }

    private void OnApplyResult(int kind, int code) => ShowResult(ClanBoards.ApplyResultText((ClanBoardAction)kind, code));

    private void OnAdvertResult(int kind, int code) => ShowResult(ClanBoards.AdvertResultText((ClanBoardAction)kind, code));

    private void ShowResult(int textId)
    {
        if (CmWindow != null) CmWindow.Visible = false;
        if (textId != 0) Notice.Show(context.Root, ItemData.Text(textId, ""));
        boardsRequests.ClanBoardOpen(cbPage);
    }

    private void OnPromote(int code, string chief, string clanName)
    {
        if (chief.Length > 0)
            context.Log.Notice(TextTemplate.Fill(ItemData.Text(ClanBoards.TextAdvertPromoted,
                "%s of %s clan is currently taking applications for new clan members. Check the clan forum for details."),
                chief, clanName));
        else if (ClanBoards.PromoteText(code) is var text and > 0)
            Notice.Show(context.Root, ItemData.Text(text, ""));
    }

    private void OnConsultation(bool connected, string name, bool received)
    {
        if (!connected || name.Length == 0)
        {
            context.Log.Notice(ItemData.Text(ChatSystem.TextWhisperFailed, "Failed to connect for a private chat"));
            return;
        }
        whispers.OpenWhisperWith(name);
        whispers.OnWhisperNotice(name, received
            ? $"{name} {ItemData.Text(ConsultReceivedText, "is requesting clan admission consultation")}"
            : $"{ItemData.Text(ConsultSentText, "Requested clan admission consultation to")} {name}");
    }

    private const int ConsultReceivedText = 16512;
    private const int ConsultSentText = 16513;

    private HudWindow BuildMessage()
    {
        var window = new HudWindow("clan_message", "Clan", new Vector2(820, 130), bodyMinWidth: MessageWidth) { Visible = false };
        window.Closed += () => { window.Visible = false; if (CbWindow is { Visible: true }) UiKit.ShowOnTop(CbWindow); };
        cbLayer.AddChild(window);
        var body = window.Body;

        var card = new HBoxContainer();
        card.AddThemeConstantOverride("separation", 8);
        body.AddChild(card);
        cmMark = new TextureRect
        {
            CustomMinimumSize = new Vector2(32, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        card.AddChild(cmMark);
        var lines = new VBoxContainer();
        card.AddChild(lines);
        cmClanName = UiTheme.Text("", 15, UiTheme.GoldBright);
        lines.AddChild(cmClanName);
        cmClassLine = UiTheme.Text("", 12, UiTheme.TextLo);
        lines.AddChild(cmClassLine);
        cmLeader = UiTheme.Text("", 12, UiTheme.TextLo);
        lines.AddChild(cmLeader);

        cmMessage = UiTheme.Text("", 13, UiTheme.TextHi);
        cmMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cmMessage.CustomMinimumSize = new Vector2(MessageWidth, 0);
        body.AddChild(cmMessage);
        cmEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(MessageWidth, MessageEditHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        cmEdit.TextChanged += () => cmCounter.Text = $"{cmEdit.Text.Length}/{ClanBoards.MaxAdvertBytes}";
        body.AddChild(cmEdit);
        cmCounter = UiTheme.Text("", 11, UiTheme.TextDim, HorizontalAlignment.Right);
        body.AddChild(cmCounter);
        cmWarning = UiTheme.Text("Enter the message you want to leave for the clan representative.", 11, UiTheme.TextDim);
        cmWarning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        cmWarning.CustomMinimumSize = new Vector2(MessageWidth, 0);
        body.AddChild(cmWarning);
        cmButtons = new HBoxContainer();
        cmButtons.AddThemeConstantOverride("separation", 6);
        body.AddChild(cmButtons);
        return window;
    }

    internal void OpenMessage(MessageMode mode, ClanAdvert? advert, string name = "", string message = "", WishRequest? request = null)
    {
        CmWindow ??= BuildMessage();
        cmMode = mode;
        cmName = name;
        bool own = mode is MessageMode.WriteAdvert or MessageMode.ViewApplicant;
        cmId = own ? clan.MyClan.ClanId : advert?.ClanId ?? request?.ClanId ?? 0;
        int markVersion = own ? clan.MyClan.MarkVersion : advert?.MarkVersion ?? request?.MarkVersion ?? 0;
        int grade = own ? clan.MyClan.Grade : advert?.Grade ?? request?.Grade ?? 0;
        int type = own ? clan.MyClan.Flag : advert?.Type ?? request?.Type ?? 0;
        cmMark.Texture = marks.Mark(cmId, markVersion);
        cmClanName.Text = own ? clan.MyClan.Name : advert?.ClanName ?? request?.ClanName ?? "";
        cmClassLine.Text = ItemData.Text(ClanBoards.ClassLineText(type), "Clan %d grade.").Replace("%d", grade.ToString());
        cmLeader.Text = "Leader: " + (mode == MessageMode.WriteAdvert ? self.LastEnter.Name
            : advert?.Chief ?? request?.Leader ?? (mode == MessageMode.ViewApplicant ? self.LastEnter.Name : ""));

        bool editing = mode is MessageMode.WriteAdvert or MessageMode.ComposeApply;
        cmEdit.Visible = cmCounter.Visible = editing;
        cmWarning.Visible = mode == MessageMode.ComposeApply;
        cmMessage.Visible = !editing;
        cmEdit.Text = "";
        cmCounter.Text = $"0/{ClanBoards.MaxAdvertBytes}";
        cmMessage.Text = mode switch
        {
            MessageMode.ViewAdvert => advert?.Text ?? "",
            MessageMode.ViewApplicant => $"{name}{ItemData.Text(ClanBoards.TextApplicantMessage, "'s Message")}\n{message}",
            MessageMode.ViewWishRequest => $"{request?.Leader}{ItemData.Text(ClanBoards.TextApplicantMessage, "'s Message")}\n{request?.Message}",
            _ => "",
        };

        foreach (var child in cmButtons.GetChildren()) { cmButtons.RemoveChild(child); child.QueueFree(); }
        switch (mode)
        {
            case MessageMode.WriteAdvert:
                cmButtons.AddChild(BoardButton("Confirm", () => SendMessage(boardsRequests.ClanAdvertRegister)));
                break;
            case MessageMode.ViewAdvert when cbStatus.State == ClanBoardState.Clanless:
                cmButtons.AddChild(BoardButton("Request to Join", () => OpenMessage(MessageMode.ComposeApply, advert)));
                cmButtons.AddChild(BoardButton("Request for Consultation", () => chatRequests.ClanConsultation(cmId)));
                break;
            case MessageMode.ViewAdvert:
                cmButtons.AddChild(BoardButton("Confirm", () => CmWindow!.Visible = false));
                break;
            case MessageMode.ComposeApply:
                cmButtons.AddChild(BoardButton("Confirm", () => SendMessage(text => boardsRequests.ClanApply(cmId, text))));
                cmButtons.AddChild(BoardButton("Request for Consultation", () => chatRequests.ClanConsultation(cmId)));
                break;
            case MessageMode.ViewApplicant:
                cmButtons.AddChild(BoardButton("Whisper", () => chatRequests.PlayerConsultation(cmName)));
                cmButtons.AddChild(BoardButton("Close", () => CmWindow!.Visible = false));
                break;
            case MessageMode.ViewWishRequest:
                cmButtons.AddChild(BoardButton("Accept", AcceptWishRequest));
                cmButtons.AddChild(BoardButton("Reject", () => { boardsRequests.WishRequestCancel(cmId); CmWindow!.Visible = false; }));
                break;
        }
        UiKit.ShowOnTop(CmWindow);
        if (editing) cmEdit.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void SendMessage(System.Action<string> send)
    {
        string text = cmEdit.Text.Trim();
        if (text.Length > ClanBoards.MaxAdvertBytes)
        {
            Notice.Show(context.Root, ItemData.Text(ClanBoards.TextOverLimit, "Length of message is over limit"));
            return;
        }
        send(text);
    }

    private void OpenJoinList()
    {
        cjWindow ??= BuildJoinList();
        cjPage = 0;
        if (CbWindow != null) CbWindow.Visible = false;
        RefreshJoinList();
        UiKit.ShowOnTop(cjWindow);
    }

    private HudWindow BuildJoinList()
    {
        var window = new HudWindow("clan_join_list", "List of Applicants", new Vector2(260, 140), bodyMinWidth: JoinListWidth) { Visible = false };
        window.Closed += () => { window.Visible = false; if (CbWindow != null) UiKit.ShowOnTop(CbWindow); };
        cbLayer.AddChild(window);
        cjRows = new VBoxContainer();
        cjRows.AddThemeConstantOverride("separation", 2);
        window.Body.AddChild(cjRows);
        var footer = new HBoxContainer();
        window.Body.AddChild(footer);
        footer.AddChild(BoardButton("Back", () => { window.Visible = false; if (CbWindow != null) UiKit.ShowOnTop(CbWindow); }));
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        footer.AddChild(BoardButton("<", () => { if (cjPage > 0) { cjPage--; RefreshJoinList(); } }));
        cjPageLabel = UiTheme.Text("1", 12, UiTheme.TextHi, HorizontalAlignment.Center);
        cjPageLabel.CustomMinimumSize = new Vector2(32, 0);
        footer.AddChild(cjPageLabel);
        footer.AddChild(BoardButton(">", () =>
        {
            if ((cjPage + 1) * ClanBoards.ApplicantsPerPage < cbStatus.Applicants.Count) { cjPage++; RefreshJoinList(); }
        }));
        return window;
    }

    private void RefreshJoinList()
    {
        foreach (var child in cjRows.GetChildren()) { cjRows.RemoveChild(child); child.QueueFree(); }
        var applicants = cbStatus.Applicants;
        for (int i = cjPage * ClanBoards.ApplicantsPerPage; i < applicants.Count && i < (cjPage + 1) * ClanBoards.ApplicantsPerPage; i++)
        {
            var applicant = applicants[i];
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var line = new HBoxContainer();
            line.AddThemeConstantOverride("separation", 8);
            panel.AddChild(line);
            line.AddChild(UiTheme.Text($"{i + 1}", 12, UiTheme.TextDim));
            line.AddChild(UiTheme.Text($"Lv {applicant.Level}", 12, UiTheme.TextLo));
            var name = UiTheme.Text(applicant.Name, 12, UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.AddChild(name);
            line.AddChild(UiTheme.Text(CharacterClassCatalog.DisplayName(applicant.Class), 12, UiTheme.TextLo));
            line.AddChild(BoardButton("Message", () => OpenMessage(MessageMode.ViewApplicant, null, applicant.Name, applicant.Message)));
            line.AddChild(BoardButton("Delete", () => boardsRequests.ClanApplyCancel(applicant.Name)));
            cjRows.AddChild(panel);
        }
        if (applicants.Count == 0) cjRows.AddChild(UiTheme.Text("No one has applied to your clan yet.", 12, UiTheme.TextLo));
        cjPageLabel.Text = (cjPage + 1).ToString();
    }

    private void AcceptWishRequest()
    {
        boardsRequests.WishAccept(cmId);
        if (CmWindow != null) CmWindow.Visible = false;
        WishRequestAccepted?.Invoke();
    }
}
