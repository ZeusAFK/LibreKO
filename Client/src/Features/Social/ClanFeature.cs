using Godot;

namespace LibreKO.Features.Social;

internal sealed partial class ClanFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly ClanSession clan;
    private readonly SelfSession self;
    private readonly IClanRequests requests;
    private readonly WhispersFeature whispers;
    private readonly ClanBoardFeature board;
    private readonly ClanCreateFeature create;
    private readonly ClanHandoverFeature handover;
    private readonly ClanMarksFeature marks;
    private readonly UserInfoFeature userInfo;
    private readonly PlayerMenuFeature playerMenu;
    private readonly ClanPointsFeature clanPoints;

    public ClanFeature(IWorldContext context, ClanSession clan, SelfSession self, IClanRequests requests, WhispersFeature whispers, ClanBoardFeature clanBoard, ClanCreateFeature clanCreate, ClanHandoverFeature clanHandover, ClanMarksFeature clanMarks, UserInfoFeature userInfo, PlayerMenuFeature playerMenu, ClanPointsFeature clanPoints)
    {
        this.context = context;
        this.clan = clan;
        this.self = self;
        this.requests = requests;
        this.whispers = whispers;
        board = clanBoard;
        create = clanCreate;
        handover = clanHandover;
        marks = clanMarks;
        this.userInfo = userInfo;
        this.playerMenu = playerMenu;
        this.clanPoints = clanPoints;
    }

    private const int ListHeight = 210;
    private const int DutyColumn = 62;
    private const int RankColumn = 34;
    private const int HoursPerDay = 24;
    private const string MsgQuit = "Successfully quitted the clan.";
    private const string MsgQuitFailed = "Failed quitting the clan.";
    private const string MsgJoined = "Successfully joined the clan.";
    private const string MsgFull = "Failed because the clan has reached the maximum number of people allowed.";
    private const string MsgInvalid = "This clan is not valid.";
    private const string MsgNoAuthority = "You do not have the authority.";
    private const string MsgAlreadyIn = "This user is already in a clan.";
    private const string MsgOtherNation = "This user is from a different nation.";
    private const string MsgDead = "This user is dead.";
    private const string MsgNoUser = "This user does not exist.";
    private const string MsgYourself = "You cannot choose yourself.";
    private const string MsgNotIn = "This user is not in the clan.";
    private const string MsgDeclined = "The user has declined.";
    private const string MsgZone = "It is not allowed in this zone.";
    private const string MsgBanned = "Banned from Clan";
    private const string MsgNoticeAuthority = "You don't have the authorization.";
    private const string MsgNoticeName = "Please confirm the exact character ID.";
    private const string MsgCommandUnavailable = "This command cannot be used.";
    private const string MsgAllianceFailed = "Failed to make an alliance.";
    private const string MsgLeaveConfirm = "Do you really want to withdraw from the clan?";

    internal enum Tab { Members, Points, Union, Top }

    internal VBoxContainer Content { get; private set; } = null!;
    private VBoxContainer mineBox = null!;
    private VBoxContainer joinBox = null!;
    private Label nameLbl = null!;
    private Label standingLbl = null!;
    private Label dutyLbl = null!;
    private Label fundLbl = null!;
    private Label noticeLbl = null!;
    private Label listHint = null!;
    private Label status = null!;
    private HBoxContainer noticeRow = null!;
    private LineEdit noticeEdit = null!;
    private VBoxContainer listBox = null!;
    private Button leaveBtn = null!;
    private Button saveBtn = null!;
    private Button allianceBtn = null!;
    private Button handoverBtn = null!;
    private readonly Dictionary<Tab, Button> tabButtons = new();
    private Tab tab = Tab.Members;
    internal bool Loaded { get; set; }
    private PopupMenu memberMenu = null!;
    private ConfirmationDialog inviteAsk = null!;
    private ConfirmationDialog disbandAsk = null!;
    internal ConfirmationDialog LeaveAsk { get; private set; } = null!;
    private ConfirmationDialog removeAsk = null!;
    private ConfirmationDialog allianceAsk = null!;
    private ConfirmationDialog confirmAsk = null!;
    private Action? confirmed;
    private CanvasLayer dialogLayer = null!;
    private string ctxMember = "";
    private int inviterId;
    private int inviteId;
    private readonly List<ClanMember> members = new();
    private List<AllianceClanEntry> allianceClans = new();
    private string allianceNotice = "";

    private bool PageVisible => context.MainWindows.IsOpen("Character") && Content is { Visible: true };

    public void Build()
    {
        BuildPage();
        clan.MyClanChangedEvent += OnMyChanged;
        clan.ClanMembersEvent += OnMembers;
        clan.ClanCreateEvent += OnCreate;
        clan.ClanInviteEvent += OnInvite;
        clan.ClanResultEvent += OnResult;
        clan.ClanNoticeRefusedEvent += OnNoticeRefused;
        clan.ClanMemoEvent += OnMemo;
        clan.ClanTop10Event += OnTop10;
        clan.ClanDonationListEvent += OnDonationList;
        clan.ClanLeaderPointsEvent += OnLeaderPoints;
        clan.ClanHandoverListEvent += OnHandoverList;
        clan.ClanHandoverEvent += OnHandover;
        clan.ClanFameEvent += OnFame;
        clan.RemovedFromClanEvent += OnRemovedFrom;
        clan.ClanMemberPresenceEvent += OnMemberPresence;
        clan.ClanStandingEvent += OnStanding;
        clan.EntityClanEvent += OnNearbyJoined;
        clan.EntityClanClearedEvent += OnNearbyLeft;
        clan.AllianceInviteEvent += OnAllianceInvite;
        clan.AllianceMembershipEvent += OnAllianceMembership;
        clan.AllianceListEvent += OnAllianceList;
        clan.ClanPointStatusEvent += OnPointStatus;
        clan.ClanDonateEvent += OnDonate;
    }

    public void Dispose()
    {
        clan.MyClanChangedEvent -= OnMyChanged;
        clan.ClanMembersEvent -= OnMembers;
        clan.ClanCreateEvent -= OnCreate;
        clan.ClanInviteEvent -= OnInvite;
        clan.ClanResultEvent -= OnResult;
        clan.ClanNoticeRefusedEvent -= OnNoticeRefused;
        clan.ClanMemoEvent -= OnMemo;
        clan.ClanTop10Event -= OnTop10;
        clan.ClanDonationListEvent -= OnDonationList;
        clan.ClanLeaderPointsEvent -= OnLeaderPoints;
        clan.ClanHandoverListEvent -= OnHandoverList;
        clan.ClanHandoverEvent -= OnHandover;
        clan.ClanFameEvent -= OnFame;
        clan.RemovedFromClanEvent -= OnRemovedFrom;
        clan.ClanMemberPresenceEvent -= OnMemberPresence;
        clan.ClanStandingEvent -= OnStanding;
        clan.EntityClanEvent -= OnNearbyJoined;
        clan.EntityClanClearedEvent -= OnNearbyLeft;
        clan.AllianceInviteEvent -= OnAllianceInvite;
        clan.AllianceMembershipEvent -= OnAllianceMembership;
        clan.AllianceListEvent -= OnAllianceList;
        clan.ClanPointStatusEvent -= OnPointStatus;
        clan.ClanDonateEvent -= OnDonate;
    }

    private void OnPointStatus(bool ok, int np, int fund)
    {
        if (ok) ApplyMy();
    }

    private void OnDonate(KnightsDonateResult result, int np, int fund, int amount)
    {
        if (result != KnightsDonateResult.Succeeded) return;
        ApplyMy();
        if (PageVisible && tab == Tab.Points) requests.DonationList();
    }

    internal void BuildPage()
    {
        Content = new VBoxContainer { CustomMinimumSize = new Vector2(UiKit.CharacterPageWidth, 0) };
        Content.AddThemeConstantOverride("separation", 8);

        BuildMineView(Content);
        BuildJoinView(Content);

        status = UiTheme.Text("", 12, UiTheme.TextLo);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        status.CustomMinimumSize = new Vector2(1, 0);
        Content.AddChild(status);

        dialogLayer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(dialogLayer);

        memberMenu = new PopupMenu();
        memberMenu.IdPressed += OnMemberMenuAction;
        dialogLayer.AddChild(memberMenu);

        inviteAsk = new ConfirmationDialog { Title = "Clan", Exclusive = false };
        inviteAsk.GetOkButton().Text = "Join";
        inviteAsk.GetCancelButton().Text = "Decline";
        inviteAsk.Confirmed += () => AnswerInvite(true);
        inviteAsk.Canceled += () => AnswerInvite(false);
        dialogLayer.AddChild(inviteAsk);

        disbandAsk = new ConfirmationDialog
        {
            Title = "Disband clan",
            DialogText = "You are the chief, so leaving disbands the clan.\nContinue?",
            Exclusive = false,
        };
        disbandAsk.GetOkButton().Text = "Disband";
        disbandAsk.Confirmed += () => requests.Destroy();
        dialogLayer.AddChild(disbandAsk);

        LeaveAsk = new ConfirmationDialog
        {
            Title = "Leave clan",
            DialogText = MsgLeaveConfirm,
            Exclusive = false,
        };
        LeaveAsk.GetOkButton().Text = "Leave";
        LeaveAsk.Confirmed += () => requests.Withdraw();
        dialogLayer.AddChild(LeaveAsk);

        removeAsk = new ConfirmationDialog { Title = "Expel", Exclusive = false };
        removeAsk.GetOkButton().Text = "Expel";
        removeAsk.Confirmed += () => { if (ctxMember.Length > 0) requests.Kick(ctxMember); };
        dialogLayer.AddChild(removeAsk);

        confirmAsk = new ConfirmationDialog { Exclusive = false };
        confirmAsk.Confirmed += () =>
        {
            var confirmed = this.confirmed;
            this.confirmed = null;
            confirmed?.Invoke();
        };
        confirmAsk.Canceled += () => confirmed = null;
        dialogLayer.AddChild(confirmAsk);

        allianceAsk = new ConfirmationDialog { Title = "Alliance", Exclusive = false };
        allianceAsk.GetOkButton().Text = "Accept";
        allianceAsk.GetCancelButton().Text = "Refuse";
        allianceAsk.Confirmed += () => requests.AllianceAnswer(true);
        allianceAsk.Canceled += () => requests.AllianceAnswer(false);
        dialogLayer.AddChild(allianceAsk);

        ApplyMy();
    }

    private void BuildMineView(VBoxContainer root)
    {
        mineBox = new VBoxContainer { Visible = false };
        mineBox.AddThemeConstantOverride("separation", 8);
        root.AddChild(mineBox);

        var card = UiTheme.Section();
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        mineBox.AddChild(card);
        var cardMargin = new MarginContainer();
        UiTheme.Margins(cardMargin, 10, 8, 10, 8);
        card.AddChild(cardMargin);
        var cardRow = new HBoxContainer();
        cardRow.AddThemeConstantOverride("separation", 10);
        cardMargin.AddChild(cardRow);
        cardRow.AddChild(marks.BuildCardMark());
        var cardCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cardCol.AddThemeConstantOverride("separation", 2);
        cardRow.AddChild(cardCol);

        nameLbl = UiTheme.Text("", 17, UiTheme.GoldBright);
        cardCol.AddChild(nameLbl);
        standingLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        cardCol.AddChild(standingLbl);
        dutyLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        cardCol.AddChild(dutyLbl);
        fundLbl = UiTheme.Text("", 12, UiTheme.Gold);
        cardCol.AddChild(fundLbl);
        cardRow.AddChild(marks.BuildSymbolButton());

        mineBox.AddChild(UiTheme.SectionTitle("Notice"));

        noticeLbl = UiTheme.Text("", 12, UiTheme.Gold);
        noticeLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        noticeLbl.CustomMinimumSize = new Vector2(1, 0);
        mineBox.AddChild(noticeLbl);

        noticeRow = new HBoxContainer();
        noticeRow.AddThemeConstantOverride("separation", 6);
        noticeEdit = new LineEdit
        {
            PlaceholderText = "clan notice",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = 200,
        };
        noticeEdit.TextSubmitted += _ => SubmitNotice();
        noticeRow.AddChild(noticeEdit);
        var noticeBtn = new Button { Text = "Set", FocusMode = Control.FocusModeEnum.None };
        noticeBtn.Pressed += SubmitNotice;
        noticeRow.AddChild(noticeBtn);
        mineBox.AddChild(noticeRow);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 2);
        mineBox.AddChild(tabs);
        foreach (var tab in new[] { Tab.Members, Tab.Points, Tab.Union, Tab.Top })
        {
            var which = tab;
            var button = UiTheme.TopTabButton(TabName(tab), 12);
            button.Pressed += () => ShowTab(which);
            tabButtons[tab] = button;
            tabs.AddChild(button);
        }

        listHint = UiTheme.Text("", 12, UiTheme.TextLo);
        listHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        listHint.CustomMinimumSize = new Vector2(1, 0);
        listHint.Visible = false;
        mineBox.AddChild(listHint);

        listBox = Scroll(mineBox, ListHeight);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        mineBox.AddChild(actions);

        var refreshBtn = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => RefreshTab();
        actions.AddChild(refreshBtn);

        var memoBtn = new Button { Text = "Memo", FocusMode = Control.FocusModeEnum.None,
            TooltipText = ItemData.Text(Domain.ClanMemo.TitleText, "Memo writing space") };
        memoBtn.Pressed += OpenMemo;
        actions.AddChild(memoBtn);

        saveBtn = new Button { Text = "Save Contribution", FocusMode = Control.FocusModeEnum.None };
        saveBtn.Pressed += clanPoints.Toggle;
        actions.AddChild(saveBtn);

        allianceBtn = new Button { Text = "Ally with target", FocusMode = Control.FocusModeEnum.None };
        allianceBtn.Pressed += OnAllianceButton;
        actions.AddChild(allianceBtn);

        var boardButton = new Button { Text = "Recruitment Board", FocusMode = Control.FocusModeEnum.None };
        boardButton.Pressed += () => board.Open();
        actions.AddChild(boardButton);

        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        handoverBtn = new Button { Text = "Hand over", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Pass the leadership to a vice-chief" };
        handoverBtn.Pressed += () => handover.Request("");
        actions.AddChild(handoverBtn);

        leaveBtn = new Button { FocusMode = Control.FocusModeEnum.None };
        leaveBtn.Pressed += OnLeave;
        actions.AddChild(leaveBtn);
    }

    private void BuildJoinView(VBoxContainer root)
    {
        joinBox = new VBoxContainer { Visible = false };
        joinBox.AddThemeConstantOverride("separation", 8);
        root.AddChild(joinBox);

        var intro = UiTheme.Text("You are not in a clan.", 14, UiTheme.TextHi);
        joinBox.AddChild(intro);
        var how = UiTheme.Text(
            "A chief can invite you from your character, or you can found a clan at an Inn Hostess "
            + $"for {ClanTypes.CreationCoins:n0} gold at level {ClanTypes.CreationLevel} or above.",
            12, UiTheme.TextLo);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        how.CustomMinimumSize = new Vector2(1, 0);
        joinBox.AddChild(how);
        var boardButton = new Button { Text = "Clan Recruitment Board", FocusMode = Control.FocusModeEnum.None };
        boardButton.Pressed += () => board.Open();
        joinBox.AddChild(boardButton);
    }

    private static VBoxContainer Scroll(VBoxContainer parent, int height)
    {
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(1, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        void Fit()
        {
            if (!GodotObject.IsInstanceValid(scroll) || !scroll.IsVisibleInTree() || FindHudWindow(scroll) is not { } window) return;
            float overflow = window.GetGlobalRect().End.Y - (scroll.GetViewportRect().Size.Y - ListScreenMargin);
            float target = Mathf.Clamp(scroll.CustomMinimumSize.Y - overflow, ListMinHeight, height);
            if (!Mathf.IsEqualApprox(target, scroll.CustomMinimumSize.Y)) scroll.CustomMinimumSize = new Vector2(1, target);
        }
        void QueueFit() => Callable.From(Fit).CallDeferred();
        scroll.VisibilityChanged += QueueFit;
        scroll.TreeEntered += () => scroll.GetViewport().SizeChanged += QueueFit;
        scroll.TreeExiting += () => scroll.GetViewport().SizeChanged -= QueueFit;
        return list;
    }

    private const float ListScreenMargin = 8f;
    private const float ListMinHeight = 96f;

    private static HudWindow? FindHudWindow(Node node)
    {
        for (var at = node.GetParent(); at != null; at = at.GetParent())
            if (at is HudWindow window) return window;
        return null;
    }

    internal void EnsureLoaded()
    {
        if (Loaded) return;
        Loaded = true;
        RefreshTab();
    }

    private void RefreshTab()
    {
        if (!clan.MyClan.InClan) return;

        switch (tab)
        {
            case Tab.Members: requests.MembersRequest(); break;
            case Tab.Points: requests.MembersRequest(); requests.DonationList(); break;
            case Tab.Top: requests.Top10(); break;
            default: requests.AllianceList(); break;
        }
    }

    private void OnMyChanged(MyClanInfo info) => ApplyMy();

    private void ApplyMy()
    {
        var info = clan.MyClan;
        mineBox.Visible = info.InClan;
        joinBox.Visible = !info.InClan;
        if (!info.InClan)
        {
            members.Clear();
            foreach (var c in listBox.GetChildren()) c.QueueFree();
            return;
        }

        nameLbl.Text = info.Name;
        string standing = ClanTypes.Standing(info.Flag, info.Grade);
        if (info.Ranking is >= 1 and <= 5) standing += $"   ·   Rank #{info.Ranking}";
        standingLbl.Text = standing;
        dutyLbl.Text =
            $"{ClanRanks.Name(info.Fame)}   ·   Members {members.Count}/{info.MaxMembers}   ·   {info.Online} online";
        fundLbl.Visible = ClanTypes.AcceptsDonations(info.Flag);
        fundLbl.Text =
            $"Clan Contribution {info.PointFund:n0}   ({info.PointFund / ClanTypes.NationalPointsPerClanPoint:n0} points)";

        string notice = info.Notice ?? "";
        bool hasNotice = notice.Length > 0;
        noticeLbl.Text = hasNotice ? notice : "No notice set.";
        noticeLbl.AddThemeColorOverride("font_color", hasNotice ? UiTheme.Gold : UiTheme.TextDim);
        noticeRow.Visible = info.IsChief;
        leaveBtn.Text = info.IsChief ? "Disband clan" : "Leave clan";
        handoverBtn.Visible = info.IsChief;
        saveBtn.Visible = ClanTypes.AcceptsDonations(info.Flag);
        allianceBtn.Visible = info.IsChief && info.Flag >= ClanTypes.Promoted && tab == Tab.Union;
        allianceBtn.Text = info.AllianceId > 0 ? "Leave alliance" : "Ally with target";

        if (tabButtons.Count > 0 && !tabButtons[tab].ButtonPressed)
            tabButtons[tab].ButtonPressed = true;
    }

    internal void ShowTab(Tab tab)
    {
        this.tab = tab;
        foreach (var (key, button) in tabButtons) button.ButtonPressed = key == tab;
        foreach (var c in listBox.GetChildren()) c.QueueFree();
        SetListHint("");
        allianceBtn.Visible = clan.MyClan.IsChief && clan.MyClan.Flag >= ClanTypes.Promoted && tab == Tab.Union;

        switch (tab)
        {
            case Tab.Members:
                RenderMembers();
                requests.MembersRequest();
                break;
            case Tab.Points:
                if (!ClanTypes.AcceptsDonations(clan.MyClan.Flag))
                {
                    SetListHint(ClanPointsFeature.MsgPointsOnlyAccredited);
                    listBox.AddChild(UiTheme.Text(
                        "Clan Contribution is the national points members save for the clan. It pays for the Knights' promotions and capes.",
                        12, UiTheme.TextDim));
                    break;
                }
                SetListHint("National points each member has saved for the clan.");
                listBox.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));
                requests.DonationList();
                break;
            case Tab.Top:
                SetListHint("The leading clans of each nation.");
                listBox.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));
                requests.Top10();
                break;
            default:
                listBox.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));
                requests.AllianceList();
                break;
        }
    }

    internal void OnMembers(List<ClanMember> members)
    {
        this.members.Clear();
        this.members.AddRange(members);
        ApplyMy();
        if (tab == Tab.Members) RenderMembers();
    }

    private void RenderMembers()
    {
        foreach (var c in listBox.GetChildren()) c.QueueFree();
        SetListHint("");
        if (members.Count == 0)
        {
            listBox.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));
            return;
        }

        var ordered = new List<ClanMember>(members);
        ordered.Sort((a, b) =>
        {
            if (a.IsOnline != b.IsOnline) return a.IsOnline ? -1 : 1;
            if (a.Fame != b.Fame) return a.Fame.CompareTo(b.Fame);
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var m in ordered)
        {
            string mName = m.Name;
            var row = new MemberRow(() => OpenMemberMenu(mName));
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var dot = UiTheme.Text("•", 15, m.IsOnline ? UiTheme.Good : UiTheme.TextDim);
            dot.CustomMinimumSize = new Vector2(10, 0);
            dot.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(dot);

            var duty = UiTheme.Text(ClanRanks.Name(m.Fame), 12, m.Fame == ClanRanks.Chief ? UiTheme.Gold : UiTheme.TextLo);
            duty.CustomMinimumSize = new Vector2(DutyColumn, 0);
            duty.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(duty);

            var name = UiTheme.Text(m.Name, 13, m.IsOnline ? UiTheme.TextHi : UiTheme.TextDim);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(name);

            var memo = UiTheme.Text(m.Memo ?? "", 11, UiTheme.TextLo);
            memo.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            memo.ClipText = true;
            memo.TooltipText = m.Memo ?? "";
            memo.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(memo);

            var sub = UiTheme.Text(
                $"{CharacterClassCatalog.DisplayName(m.Class)}  Lv {m.Level}", 12, m.IsOnline ? UiTheme.TextLo : UiTheme.TextDim);
            sub.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(sub);

            var seen = UiTheme.Text(m.IsOnline ? "" : LastSeen(m.HoursSinceLogin), 11, UiTheme.TextDim);
            seen.CustomMinimumSize = new Vector2(RankColumn, 0);
            seen.HorizontalAlignment = HorizontalAlignment.Right;
            seen.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(seen);
            listBox.AddChild(row);
        }
    }

    private void OpenMemo()
    {
        string mine = members.Find(m => string.Equals(m.Name, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase)).Memo ?? "";
        Notice.AskText(context.Root, ItemData.Text(Domain.ClanMemo.PromptText, "Enter Memo"), typed => requests.Memo(typed.Trim()),
            ItemData.Text(Domain.ClanMemo.TitleText, "Memo writing space"), initial: mine,
            placeholder: ItemData.Text(Domain.ClanMemo.PlaceholderText, "Please enter the Memo"), maxLength: Domain.ClanMemo.MaxLength);
    }

    private void OnMemo(byte result, string memo)
    {
        if (result != Domain.ClanMemo.Written)
        {
            context.Log.Notice(ItemData.Text(Domain.ClanMemo.FailureText(result), "You have failed to register a memo."));
            return;
        }
        int index = members.FindIndex(m => string.Equals(m.Name, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return;
        var mine = members[index];
        mine.Memo = memo;
        members[index] = mine;
        if (tab == Tab.Members) RenderMembers();
    }

    private static string LastSeen(int hours) =>
        hours < HoursPerDay ? $"{hours}h" : $"{hours / HoursPerDay}d";

    private void OpenMemberMenu(string name)
    {
        bool isSelf = string.Equals(name, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase);
        ctxMember = name;
        memberMenu.Clear();
        memberMenu.AddItem($"— {name} —", 0);
        memberMenu.SetItemDisabled(0, true);
        if (!isSelf)
        {
            memberMenu.AddItem("Whisper", 1);
            memberMenu.AddItem("Invite to party", 2);
            memberMenu.AddItem("User information", 3);
        }
        if (clan.MyClan.IsChief && !isSelf)
        {
            memberMenu.AddSeparator();
            memberMenu.AddItem("Appoint vice-chief", 4);
            memberMenu.AddItem("Hand over leadership", 5);
            memberMenu.AddItem("Expel from clan", 6);
        }
        if (memberMenu.ItemCount <= 1) return;
        UiKit.PopupAtCursor(context.Root, memberMenu, context.Root.GetViewport().GetMousePosition());
    }

    private void OnMemberMenuAction(long id)
    {
        if (ctxMember.Length == 0) return;
        switch (id)
        {
            case 1: whispers.OpenWhisperWith(ctxMember); break;
            case 2: playerMenu.InviteToParty(ctxMember); break;
            case 3: userInfo.RequestInformation(ctxMember); break;
            case 4: requests.PromoteVice(ctxMember); break;
            case 5:
                handover.Request(ctxMember);
                break;
            case 6:
                removeAsk.DialogText = $"Do you really want to expel {ctxMember}?";
                removeAsk.PopupCentered();
                break;
        }
    }

    private static string TabName(Tab tab) => tab switch
    {
        Tab.Members => "Members",
        Tab.Points => "Contribution",
        Tab.Top => "Top 10",
        _ => "Union",
    };

    private void SubmitNotice()
    {
        string text = noticeEdit.Text.Trim();
        if (text.Length == 0) return;
        requests.Notice(text);
        noticeEdit.Clear();
    }

    private void OnLeave()
    {
        if (!clan.MyClan.InClan) return;
        if (clan.MyClan.IsChief) { disbandAsk.PopupCentered(); return; }
        LeaveAsk.PopupCentered();
    }

    internal void OnDonationList(List<(string Name, int Points)> list)
    {
        if (!clan.MyClan.InClan || tab != Tab.Points) return;

        foreach (var c in listBox.GetChildren()) c.QueueFree();
        if (list.Count == 0)
        {
            listBox.AddChild(UiTheme.Text("Nobody has saved Contribution yet.", 13, UiTheme.TextDim));
            return;
        }

        int position = 1;
        foreach (var (name, points) in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var pos = UiTheme.Text($"#{position++}", 12, UiTheme.TextLo);
            pos.CustomMinimumSize = new Vector2(RankColumn, 0);
            hb.AddChild(pos);
            var who = UiTheme.Text(name, 13, UiTheme.TextHi);
            who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(who);
            hb.AddChild(UiTheme.Text($"{points:n0}", 12, UiTheme.Gold));
            listBox.AddChild(row);
        }
    }

    private void OnLeaderPoints(List<(string Name, int Points)> list) => OnDonationList(list);

    internal void OnTop10(List<(int Nation, int Rank, string Name)> top)
    {
        if (!clan.MyClan.InClan || tab != Tab.Top) return;
        var target = listBox;
        foreach (var c in target.GetChildren()) c.QueueFree();
        if (top.Count == 0)
        {
            target.AddChild(UiTheme.Text("No ranked clans yet.", 13, UiTheme.TextDim));
            return;
        }

        foreach (var (nation, position, name) in top)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var pos = UiTheme.Text($"#{position + 1}", 12, UiTheme.TextLo);
            pos.CustomMinimumSize = new Vector2(RankColumn, 0);
            hb.AddChild(pos);
            var who = UiTheme.Text(name, 13, UiTheme.TextHi);
            who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(who);
            hb.AddChild(UiTheme.Text(Nations.Name(nation), 12, UiKit.NationColor(nation)));
            target.AddChild(row);
        }
    }

    private void OnCreate(bool ok, KnightsResult code, string name)
    {
        create.Close();
        if (ok)
        {
            context.Log.Notice("You are now a leader of a clan. Congratulations!!!");
            Loaded = true;
            requests.MembersRequest();
            return;
        }

        string text = code switch
        {
            KnightsResult.NoSuchUser => "Sorry. A weakling like you are not fit to become a leader!!",
            KnightsResult.UserIsDead => "Oh~ I'm sorry, but somebody else is already using that name. Try a different name.",
            KnightsResult.DifferentNation => $"Sorry. You need {ClanTypes.CreationCoins:n0} gold in order to create a clan.",
            KnightsResult.AlreadyInClan => "You can't create a clan because you're already in another clan.",
            KnightsResult.ClanNotValid => "You cannot create a clan today.",
            KnightsResult.ClanFull => "Creating a clan is only allowed in the 1st server group.",
            KnightsResult.CannotChooseYourself => "Creating a clan is only allowed in your nation's village or Moradon.",
            _ => "Hm.. You can't create a clan right now. Please come back later.",
        };
        context.Log.Notice(text);
        if (code == KnightsResult.UserIsDead) create.Open(text);
    }

    private void OnInvite(int inviterId, int clanId, string clanName)
    {
        this.inviterId = inviterId;
        inviteId = clanId;
        inviteAsk.DialogText = $"Will you join the clan {clanName} ?";
        inviteAsk.PopupCentered();
    }

    private void AnswerInvite(bool accept)
    {
        if (inviterId == 0) return;
        requests.InviteAnswer(accept, inviterId, inviteId);
        inviterId = 0;
        inviteId = 0;
    }

    private void OnResult(KnightsSubOpcode sub, KnightsResult code)
    {
        bool ok = code == KnightsResult.Succeeded;
        switch (sub)
        {
            case KnightsSubOpcode.Join:
                if (ok) { context.Log.Notice(MsgJoined); Loaded = true; requests.MembersRequest(); }
                else context.Log.Notice(Refusal(code));
                return;
            case KnightsSubOpcode.Withdraw:
                context.Log.Notice(ok ? MsgQuit : MsgQuitFailed);
                if (!ok) context.Log.Notice(code == KnightsResult.NotInThisZone ? MsgZone : MsgQuitFailed);
                return;
            case KnightsSubOpcode.Destroy:
                if (ok) context.Log.Notice("The clan has been disbanded.");
                else context.Log.Notice(Refusal(code));
                return;
            case KnightsSubOpcode.Remove:
                if (ok) requests.MembersRequest();
                else context.Log.Notice(Refusal(code));
                return;
            case KnightsSubOpcode.Reject:
                context.Log.Notice(MsgDeclined);
                return;
            case KnightsSubOpcode.AllyCreate:
            case KnightsSubOpcode.AllyReq:
            case KnightsSubOpcode.AllyInsert:
            case KnightsSubOpcode.AllyRemove:
            case KnightsSubOpcode.AllyPunish:
                context.Log.Notice(MsgAllianceFailed);
                return;
            default:
                if (ok) requests.MembersRequest();
                else context.Log.Notice(Refusal(code));
                return;
        }
    }

    private static string Refusal(KnightsResult code) => code switch
    {
        KnightsResult.NoSuchUser => MsgNoUser,
        KnightsResult.UserIsDead => MsgDead,
        KnightsResult.DifferentNation => MsgOtherNation,
        KnightsResult.AlreadyInClan => MsgAlreadyIn,
        KnightsResult.NoAuthority => MsgNoAuthority,
        KnightsResult.ClanNotValid => MsgInvalid,
        KnightsResult.ClanFull => MsgFull,
        KnightsResult.CannotChooseYourself => MsgYourself,
        KnightsResult.NotInClan => MsgNotIn,
        KnightsResult.UserDeclined => MsgDeclined,
        KnightsResult.NotInThisZone => MsgZone,
        (KnightsResult)16 => MsgBanned,
        _ => MsgCommandUnavailable,
    };

    private void OnNoticeRefused(int code) => context.Log.Notice(code switch
    {
        1 => MsgNoticeAuthority,
        3 => MsgNoticeName,
        _ => MsgCommandUnavailable,
    });

    private void OnHandoverList(KnightsResult state, List<string> names) => handover.ShowHandoverWindow((byte)state, names);

    private void OnHandover(bool ok, string oldChief, string newChief)
    {
        if (!ok) { context.Log.Notice(handover.HandoverFailedText); return; }
        context.Log.Notice(handover.HandoverDoneText(oldChief, newChief));
        requests.MembersRequest();
    }

    private void OnRemovedFrom() => context.Log.Notice(MsgBanned);

    private void OnFame(int charId, int clanId, byte fame)
    {
        if (charId == context.Self.CharId)
        {
            if (clanId == 0) return;
            context.Log.Notice($"You are now {ClanRanks.Name(fame)} of the clan.");
            if (PageVisible) requests.MembersRequest();
            return;
        }

        if (clanId == 0) { OnNearbyLeft(charId); return; }
        for (int i = 0; i < members.Count; i++)
        {
            if (!context.Entities.Views.TryGetValue(charId, out var ent) || !string.Equals(members[i].Name, ent.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            var member = members[i];
            member.Fame = fame;
            members[i] = member;
        }
        if (PageVisible && tab == Tab.Members) RenderMembers();
    }

    private void OnMemberPresence(string name, bool online)
    {
        context.Log.Notice(online ? $"{name} is online." : $"{name} is offline.");
        for (int i = 0; i < members.Count; i++)
        {
            if (!string.Equals(members[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            var member = members[i];
            member.IsOnline = online;
            member.HoursSinceLogin = 0;
            members[i] = member;
        }
        if (PageVisible && tab == Tab.Members) RenderMembers();
    }

    private void OnStanding(List<ClanStanding> standings)
    {
        if (PageVisible) ApplyMy();
    }

    private void OnNearbyJoined(EntityClanChange change)
    {
        if (change.CharId == context.Self.CharId || !context.Entities.Views.ContainsKey(change.CharId)) return;
        if (clan.MyClan.InClan && change.ClanId == clan.MyClan.ClanId && PageVisible) requests.MembersRequest();
    }

    private void OnNearbyLeft(int charId)
    {
        if (charId == context.Self.CharId || !context.Entities.Views.TryGetValue(charId, out var ent)) return;
        bool wasMine = clan.MyClan.InClan && ent.KnightsId == clan.MyClan.ClanId;
        if (wasMine && PageVisible) requests.MembersRequest();
    }

    private void AskConfirm(string title, string ok, string text, Action confirmed)
    {
        this.confirmed = confirmed;
        confirmAsk.Title = title;
        confirmAsk.GetOkButton().Text = ok;
        confirmAsk.DialogText = text;
        confirmAsk.PopupCentered();
    }

    private void OnAllianceButton()
    {
        if (!clan.MyClan.IsChief) return;
        if (clan.MyClan.AllianceId > 0)
        {
            AskConfirm("Leave alliance", "Leave",
                clan.MyClan.AllianceId == clan.MyClan.ClanId
                    ? "Your clan leads this alliance. Leave it anyway?"
                    : "Leave the alliance?",
                requests.AllianceLeave);
            return;
        }

        if (context.Selection.SelectedId <= 0 || !context.Entities.Views.TryGetValue(context.Selection.SelectedId, out var target) || target.IsNpc)
        {
            context.Log.Notice("Target the chief of the clan you want to ally with.");
            return;
        }

        if (clan.MyClan.AllianceId == clan.MyClan.ClanId) requests.AllianceInsert(context.Selection.SelectedId);
        else requests.AllianceCreate(context.Selection.SelectedId);
        SetStatus($"Alliance request sent to {target.Name}.", false);
    }

    private void OnAllianceInvite(string clanName, int clanId)
    {
        allianceAsk.DialogText = $"The Knights {clanName} has sent a request for confederacy.  Will you accept?";
        allianceAsk.PopupCentered();
    }

    private void OnAllianceMembership(int allianceId, int clanId, bool joined)
    {
        ApplyMy();
        if (PageVisible && tab == Tab.Union) requests.AllianceList();
    }

    private void OnAllianceList(string notice, List<AllianceClanEntry> clans)
    {
        allianceNotice = notice;
        allianceClans = clans;
        if (!clan.MyClan.InClan || tab != Tab.Union) return;

        foreach (var c in listBox.GetChildren()) c.QueueFree();
        if (clans.Count == 0)
        {
            SetListHint(clan.MyClan.IsChief && clan.MyClan.Flag >= ClanTypes.Promoted
                ? "Your Knights are in no alliance. Target another chief and press Ally with target."
                : "Your Knights are in no alliance.");
            return;
        }

        SetListHint(notice.Length > 0 ? notice : "Alliance");
        foreach (var allianceClan in clans)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 0);
            row.AddChild(col);
            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
            col.AddChild(head);
            var name = UiTheme.Text(allianceClan.Name, 13, allianceClan.Id == clan.MyClan.ClanId ? UiTheme.GoldBright : UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(name);
            head.AddChild(UiTheme.Text(allianceClan.Id == clan.MyClan.AllianceId ? "leads" : "member", 11, UiTheme.TextLo));
            if (clan.MyClan.IsChief && clan.MyClan.AllianceId == clan.MyClan.ClanId && allianceClan.Id != clan.MyClan.ClanId)
            {
                int punishId = allianceClan.Id;
                string punishName = allianceClan.Name;
                var kick = new Button { Text = "Expel", FocusMode = Control.FocusModeEnum.None };
                kick.Pressed += () => AskConfirm("Expel from alliance", "Expel",
                    $"Expel {punishName} from the alliance?", () => requests.AlliancePunish(punishId));
                head.AddChild(kick);
            }
            var officers = new List<string>();
            foreach (var officer in allianceClan.Officers) officers.Add($"{ClanRanks.Name(officer.Fame)} {officer.Name}");
            var line = UiTheme.Text(string.Join("   ·   ", officers), 11, UiTheme.TextLo);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.CustomMinimumSize = new Vector2(1, 0);
            col.AddChild(line);
            listBox.AddChild(row);
        }
    }

    private void SetListHint(string text)
    {
        listHint.Text = text;
        listHint.Visible = text.Length > 0;
    }

    private void SetStatus(string text, bool warn)
    {
        status.Text = text;
        status.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private sealed partial class MemberRow : PanelContainer
    {
        private readonly System.Action onRightClick;
        public MemberRow(System.Action onRightClick) => this.onRightClick = onRightClick;

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                onRightClick();
        }
    }
}
