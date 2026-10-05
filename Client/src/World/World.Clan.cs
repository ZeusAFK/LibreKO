using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int ClanListHeight = 210;
    private const int ClanDutyColumn = 62;
    private const int ClanRankColumn = 34;
    private const int HoursPerDay = 24;

    private const string ClanMsgQuit = "Successfully quitted the clan.";
    private const string ClanMsgQuitFailed = "Failed quitting the clan.";
    private const string ClanMsgJoined = "Successfully joined the clan.";
    private const string ClanMsgFull = "Failed because the clan has reached the maximum number of people allowed.";
    private const string ClanMsgInvalid = "This clan is not valid.";
    private const string ClanMsgNoAuthority = "You do not have the authority.";
    private const string ClanMsgAlreadyInClan = "This user is already in a clan.";
    private const string ClanMsgOtherNation = "This user is from a different nation.";
    private const string ClanMsgDead = "This user is dead.";
    private const string ClanMsgNoUser = "This user does not exist.";
    private const string ClanMsgYourself = "You cannot choose yourself.";
    private const string ClanMsgNotInClan = "This user is not in the clan.";
    private const string ClanMsgDeclined = "The user has declined.";
    private const string ClanMsgZone = "It is not allowed in this zone.";
    private const string ClanMsgBanned = "Banned from Clan";
    private const string ClanMsgNoticeAuthority = "You don't have the authorization.";
    private const string ClanMsgNoticeName = "Please confirm the exact character ID.";
    private const string ClanMsgCommandUnavailable = "This command cannot be used.";
    private const string ClanMsgHandoverAuthority = "You don't have the authority to change leadership.";
    private const string ClanMsgHandoverNoVice = "There is no Co-leader authorized to change leadership.";
    private const string ClanMsgHandoverFailed = "You failed to pass on the leadership.";
    private const string ClanMsgAllianceFailed = "Failed to make an alliance.";
    private const string ClanMsgPointsOnlyAccredited = "Accredited Knights Grade 5 or above can save-up Contribution.";
    private const string ClanMsgLeaveConfirm = "Do you really want to withdraw from the clan?";

    internal enum ClanTab { Members, Points, Union }

    private VBoxContainer _clanContent = null!;
    private VBoxContainer _clanMineBox = null!, _clanJoinBox = null!;
    private Label _clanNameLbl = null!, _clanStandingLbl = null!, _clanDutyLbl = null!, _clanFundLbl = null!;
    private Label _clanNoticeLbl = null!, _clanListHint = null!, _clanStatus = null!;
    private HBoxContainer _clanNoticeRow = null!;
    private LineEdit _clanNoticeEdit = null!;
    private VBoxContainer _clanList = null!;
    private Button _clanLeaveBtn = null!, _clanSaveBtn = null!, _clanAllianceBtn = null!;
    private readonly Dictionary<ClanTab, Button> _clanTabButtons = new();
    private ClanTab _clanTab = ClanTab.Members;
    private bool _clanLoaded;

    private PopupMenu _clanMemberMenu = null!;
    private ConfirmationDialog _clanInviteAsk = null!, _clanDisbandAsk = null!, _clanLeaveAsk = null!, _clanRemoveAsk = null!, _clanAllianceAsk = null!;
    private ConfirmationDialog _clanConfirmAsk = null!;
    private Action? _clanConfirmed;
    private CanvasLayer _clanDialogLayer = null!;
    private string _ctxMember = "";
    private int _clanInviterId;
    private int _clanInviteClanId;
    private readonly List<ClanMember> _clanMembers = new();
    private List<AllianceClanEntry> _allianceClans = new();
    private string _allianceNotice = "";

    private MyClanInfo MyClan => Net.I.MyClan;

    private bool ClanPageVisible => MainWindowOpen("Character") && _clanContent is { Visible: true };

    private void ClanInit()
    {
        BuildClanPage();
        Net.I.MyClanChangedEvent += OnMyClanChanged;
        Net.I.ClanMembersEvent += OnClanMembers;
        Net.I.ClanCreateEvent += OnClanCreate;
        Net.I.ClanInviteEvent += OnClanInvite;
        Net.I.ClanResultEvent += OnClanResult;
        Net.I.ClanNoticeRefusedEvent += OnClanNoticeRefused;
        Net.I.ClanTop10Event += OnClanTop10;
        Net.I.ClanDonationListEvent += OnClanDonationList;
        Net.I.ClanLeaderPointsEvent += OnClanLeaderPoints;
        Net.I.ClanHandoverListEvent += OnClanHandoverList;
        Net.I.ClanHandoverEvent += OnClanHandover;
        Net.I.ClanFameEvent += OnClanFame;
        Net.I.RemovedFromClanEvent += OnRemovedFromClan;
        Net.I.ClanMemberPresenceEvent += OnClanMemberPresence;
        Net.I.ClanStandingEvent += OnClanStanding;
        Net.I.EntityClanEvent += OnEntityClan;
        Net.I.EntityClanClearedEvent += OnEntityClanCleared;
        Net.I.AllianceInviteEvent += OnAllianceInvite;
        Net.I.AllianceMembershipEvent += OnAllianceMembership;
        Net.I.AllianceListEvent += OnAllianceList;
        ClanPointsInit();
        ClanCreateInit();
    }

    private void ClanDispose()
    {
        Net.I.MyClanChangedEvent -= OnMyClanChanged;
        Net.I.ClanMembersEvent -= OnClanMembers;
        Net.I.ClanCreateEvent -= OnClanCreate;
        Net.I.ClanInviteEvent -= OnClanInvite;
        Net.I.ClanResultEvent -= OnClanResult;
        Net.I.ClanNoticeRefusedEvent -= OnClanNoticeRefused;
        Net.I.ClanTop10Event -= OnClanTop10;
        Net.I.ClanDonationListEvent -= OnClanDonationList;
        Net.I.ClanLeaderPointsEvent -= OnClanLeaderPoints;
        Net.I.ClanHandoverListEvent -= OnClanHandoverList;
        Net.I.ClanHandoverEvent -= OnClanHandover;
        Net.I.ClanFameEvent -= OnClanFame;
        Net.I.RemovedFromClanEvent -= OnRemovedFromClan;
        Net.I.ClanMemberPresenceEvent -= OnClanMemberPresence;
        Net.I.ClanStandingEvent -= OnClanStanding;
        Net.I.EntityClanEvent -= OnEntityClan;
        Net.I.EntityClanClearedEvent -= OnEntityClanCleared;
        Net.I.AllianceInviteEvent -= OnAllianceInvite;
        Net.I.AllianceMembershipEvent -= OnAllianceMembership;
        Net.I.AllianceListEvent -= OnAllianceList;
        ClanPointsDispose();
        ClanCreateDispose();
    }

    private void BuildClanPage()
    {
        _clanContent = new VBoxContainer { CustomMinimumSize = new Vector2(CharacterPageWidth, 0) };
        _clanContent.AddThemeConstantOverride("separation", 8);

        BuildClanMineView(_clanContent);
        BuildClanJoinView(_clanContent);

        _clanStatus = UiTheme.Text("", 12, UiTheme.TextLo);
        _clanStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _clanStatus.CustomMinimumSize = new Vector2(1, 0);
        _clanContent.AddChild(_clanStatus);

        _clanDialogLayer = new CanvasLayer { Layer = 75 };
        AddChild(_clanDialogLayer);

        _clanMemberMenu = new PopupMenu();
        _clanMemberMenu.IdPressed += OnMemberMenuAction;
        _clanDialogLayer.AddChild(_clanMemberMenu);

        _clanInviteAsk = new ConfirmationDialog { Title = "Clan", Exclusive = false };
        _clanInviteAsk.GetOkButton().Text = "Join";
        _clanInviteAsk.GetCancelButton().Text = "Decline";
        _clanInviteAsk.Confirmed += () => AnswerClanInvite(true);
        _clanInviteAsk.Canceled += () => AnswerClanInvite(false);
        _clanDialogLayer.AddChild(_clanInviteAsk);

        _clanDisbandAsk = new ConfirmationDialog
        {
            Title = "Disband clan",
            DialogText = "You are the chief, so leaving disbands the clan.\nContinue?",
            Exclusive = false,
        };
        _clanDisbandAsk.GetOkButton().Text = "Disband";
        _clanDisbandAsk.Confirmed += () => Net.I.SendClanDestroy();
        _clanDialogLayer.AddChild(_clanDisbandAsk);

        _clanLeaveAsk = new ConfirmationDialog
        {
            Title = "Leave clan",
            DialogText = ClanMsgLeaveConfirm,
            Exclusive = false,
        };
        _clanLeaveAsk.GetOkButton().Text = "Leave";
        _clanLeaveAsk.Confirmed += () => Net.I.SendClanWithdraw();
        _clanDialogLayer.AddChild(_clanLeaveAsk);

        _clanRemoveAsk = new ConfirmationDialog { Title = "Expel", Exclusive = false };
        _clanRemoveAsk.GetOkButton().Text = "Expel";
        _clanRemoveAsk.Confirmed += () => { if (_ctxMember.Length > 0) Net.I.SendClanKick(_ctxMember); };
        _clanDialogLayer.AddChild(_clanRemoveAsk);

        _clanConfirmAsk = new ConfirmationDialog { Exclusive = false };
        _clanConfirmAsk.Confirmed += () =>
        {
            var confirmed = _clanConfirmed;
            _clanConfirmed = null;
            confirmed?.Invoke();
        };
        _clanConfirmAsk.Canceled += () => _clanConfirmed = null;
        _clanDialogLayer.AddChild(_clanConfirmAsk);

        _clanAllianceAsk = new ConfirmationDialog { Title = "Alliance", Exclusive = false };
        _clanAllianceAsk.GetOkButton().Text = "Accept";
        _clanAllianceAsk.GetCancelButton().Text = "Refuse";
        _clanAllianceAsk.Confirmed += () => Net.I.SendAllianceAnswer(true);
        _clanAllianceAsk.Canceled += () => Net.I.SendAllianceAnswer(false);
        _clanDialogLayer.AddChild(_clanAllianceAsk);

        ApplyMyClan();
    }

    private void BuildClanMineView(VBoxContainer root)
    {
        _clanMineBox = new VBoxContainer { Visible = false };
        _clanMineBox.AddThemeConstantOverride("separation", 8);
        root.AddChild(_clanMineBox);

        var card = UiTheme.Section();
        card.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _clanMineBox.AddChild(card);
        var cardMargin = new MarginContainer();
        UiTheme.Margins(cardMargin, 10, 8, 10, 8);
        card.AddChild(cardMargin);
        var cardCol = new VBoxContainer();
        cardCol.AddThemeConstantOverride("separation", 2);
        cardMargin.AddChild(cardCol);

        _clanNameLbl = UiTheme.Text("", 17, UiTheme.GoldBright);
        cardCol.AddChild(_clanNameLbl);
        _clanStandingLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        cardCol.AddChild(_clanStandingLbl);
        _clanDutyLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        cardCol.AddChild(_clanDutyLbl);
        _clanFundLbl = UiTheme.Text("", 12, UiTheme.Gold);
        cardCol.AddChild(_clanFundLbl);

        _clanMineBox.AddChild(UiTheme.SectionTitle("Notice"));

        _clanNoticeLbl = UiTheme.Text("", 12, UiTheme.Gold);
        _clanNoticeLbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _clanNoticeLbl.CustomMinimumSize = new Vector2(1, 0);
        _clanMineBox.AddChild(_clanNoticeLbl);

        _clanNoticeRow = new HBoxContainer();
        _clanNoticeRow.AddThemeConstantOverride("separation", 6);
        _clanNoticeEdit = new LineEdit
        {
            PlaceholderText = "clan notice",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MaxLength = 200,
        };
        _clanNoticeEdit.TextSubmitted += _ => SubmitClanNotice();
        _clanNoticeRow.AddChild(_clanNoticeEdit);
        var noticeBtn = new Button { Text = "Set", FocusMode = Control.FocusModeEnum.None };
        noticeBtn.Pressed += SubmitClanNotice;
        _clanNoticeRow.AddChild(noticeBtn);
        _clanMineBox.AddChild(_clanNoticeRow);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 2);
        _clanMineBox.AddChild(tabs);
        foreach (var tab in new[] { ClanTab.Members, ClanTab.Points, ClanTab.Union })
        {
            var which = tab;
            var button = UiTheme.TopTabButton(ClanTabName(tab), 12);
            button.Pressed += () => ShowClanTab(which);
            _clanTabButtons[tab] = button;
            tabs.AddChild(button);
        }

        _clanListHint = UiTheme.Text("", 12, UiTheme.TextLo);
        _clanListHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _clanListHint.CustomMinimumSize = new Vector2(1, 0);
        _clanListHint.Visible = false;
        _clanMineBox.AddChild(_clanListHint);

        _clanList = ClanScroll(_clanMineBox, ClanListHeight);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        _clanMineBox.AddChild(actions);

        var refreshBtn = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => RefreshClanTab();
        actions.AddChild(refreshBtn);

        _clanSaveBtn = new Button { Text = "Save Contribution", FocusMode = Control.FocusModeEnum.None };
        _clanSaveBtn.Pressed += () => ToggleClanPoints();
        actions.AddChild(_clanSaveBtn);

        _clanAllianceBtn = new Button { Text = "Ally with target", FocusMode = Control.FocusModeEnum.None };
        _clanAllianceBtn.Pressed += OnAllianceButton;
        actions.AddChild(_clanAllianceBtn);

        actions.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        _clanLeaveBtn = new Button { FocusMode = Control.FocusModeEnum.None };
        _clanLeaveBtn.Pressed += OnLeaveClan;
        actions.AddChild(_clanLeaveBtn);
    }

    private void BuildClanJoinView(VBoxContainer root)
    {
        _clanJoinBox = new VBoxContainer { Visible = false };
        _clanJoinBox.AddThemeConstantOverride("separation", 8);
        root.AddChild(_clanJoinBox);

        var intro = UiTheme.Text("You are not in a clan.", 14, UiTheme.TextHi);
        _clanJoinBox.AddChild(intro);
        var how = UiTheme.Text(
            "A chief can invite you from your character, or you can found a clan at an Inn Hostess "
            + $"for {ClanTypes.CreationCoins:n0} gold at level {ClanTypes.CreationLevel} or above.",
            12, UiTheme.TextLo);
        how.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        how.CustomMinimumSize = new Vector2(1, 0);
        _clanJoinBox.AddChild(how);
    }

    private static VBoxContainer ClanScroll(VBoxContainer parent, int height)
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
        return list;
    }

    private void EnsureClanLoaded()
    {
        if (_clanLoaded) return;
        _clanLoaded = true;
        RefreshClanTab();
    }

    private void RefreshClanTab()
    {
        if (!MyClan.InClan) return;

        switch (_clanTab)
        {
            case ClanTab.Members: Net.I.SendClanMembersRequest(); break;
            case ClanTab.Points: Net.I.SendClanMembersRequest(); Net.I.SendClanDonationList(); break;
            default: Net.I.SendAllianceList(); break;
        }
    }

    private void OnMyClanChanged(MyClanInfo info)
    {
        ApplyMyClan();
        ApplySelfClan(info.InClan ? info.Name : "");
        if (_self != null)
            AttachClanGauntlet(_self, Net.I.LastEnter.Race, info.InClan ? info.Grade : 0, info.InClan ? info.Ranking : 0);
    }

    private void ApplyMyClan()
    {
        var info = MyClan;
        _clanMineBox.Visible = info.InClan;
        _clanJoinBox.Visible = !info.InClan;
        if (!info.InClan)
        {
            _clanMembers.Clear();
            foreach (var c in _clanList.GetChildren()) c.QueueFree();
            return;
        }

        _clanNameLbl.Text = info.Name;
        string standing = ClanTypes.Standing(info.Flag, info.Grade);
        if (info.Ranking is >= 1 and <= 5) standing += $"   ·   Rank #{info.Ranking}";
        _clanStandingLbl.Text = standing;
        _clanDutyLbl.Text =
            $"{ClanRanks.Name(info.Fame)}   ·   Members {_clanMembers.Count}/{info.MaxMembers}   ·   {info.Online} online";
        _clanFundLbl.Visible = ClanTypes.AcceptsDonations(info.Flag);
        _clanFundLbl.Text =
            $"Clan Contribution {info.PointFund:n0}   ({info.PointFund / ClanTypes.NationalPointsPerClanPoint:n0} points)";

        string notice = info.Notice ?? "";
        bool hasNotice = notice.Length > 0;
        _clanNoticeLbl.Text = hasNotice ? notice : "No notice set.";
        _clanNoticeLbl.AddThemeColorOverride("font_color", hasNotice ? UiTheme.Gold : UiTheme.TextDim);
        _clanNoticeRow.Visible = info.IsChief;
        _clanLeaveBtn.Text = info.IsChief ? "Disband clan" : "Leave clan";
        _clanSaveBtn.Visible = ClanTypes.AcceptsDonations(info.Flag);
        _clanAllianceBtn.Visible = info.IsChief && info.Flag >= ClanTypes.Promoted && _clanTab == ClanTab.Union;
        _clanAllianceBtn.Text = info.AllianceId > 0 ? "Leave alliance" : "Ally with target";

        if (_clanTabButtons.Count > 0 && !_clanTabButtons[_clanTab].ButtonPressed)
            _clanTabButtons[_clanTab].ButtonPressed = true;
    }

    private void ShowClanTab(ClanTab tab)
    {
        _clanTab = tab;
        foreach (var (key, button) in _clanTabButtons) button.ButtonPressed = key == tab;
        foreach (var c in _clanList.GetChildren()) c.QueueFree();
        SetClanListHint("");
        _clanAllianceBtn.Visible = MyClan.IsChief && MyClan.Flag >= ClanTypes.Promoted && tab == ClanTab.Union;

        switch (tab)
        {
            case ClanTab.Members:
                RenderClanMembers();
                Net.I.SendClanMembersRequest();
                break;
            case ClanTab.Points:
                if (!ClanTypes.AcceptsDonations(MyClan.Flag))
                {
                    SetClanListHint(ClanMsgPointsOnlyAccredited);
                    _clanList.AddChild(UiTheme.Text(
                        "Clan Contribution is the national points members save for the clan. It pays for the Knights' promotions and capes.",
                        12, UiTheme.TextDim));
                    break;
                }
                SetClanListHint("National points each member has saved for the clan.");
                _clanList.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));
                Net.I.SendClanDonationList();
                break;
            default:
                _clanList.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));
                Net.I.SendAllianceList();
                break;
        }
    }

    private void OnClanMembers(List<ClanMember> members)
    {
        _clanMembers.Clear();
        _clanMembers.AddRange(members);
        ApplyMyClan();
        if (_clanTab == ClanTab.Members) RenderClanMembers();
    }

    private void RenderClanMembers()
    {
        foreach (var c in _clanList.GetChildren()) c.QueueFree();
        SetClanListHint("");
        if (_clanMembers.Count == 0)
        {
            _clanList.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));
            return;
        }

        var ordered = new List<ClanMember>(_clanMembers);
        ordered.Sort((a, b) =>
        {
            if (a.IsOnline != b.IsOnline) return a.IsOnline ? -1 : 1;
            if (a.Fame != b.Fame) return a.Fame.CompareTo(b.Fame);
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var m in ordered)
        {
            string mName = m.Name;
            var row = new ClanMemberRow(() => OpenMemberMenu(mName));
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var dot = UiTheme.Text("•", 15, m.IsOnline ? UiTheme.Good : UiTheme.TextDim);
            dot.CustomMinimumSize = new Vector2(10, 0);
            dot.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(dot);

            var duty = UiTheme.Text(ClanRanks.Name(m.Fame), 12, m.Fame == ClanRanks.Chief ? UiTheme.Gold : UiTheme.TextLo);
            duty.CustomMinimumSize = new Vector2(ClanDutyColumn, 0);
            duty.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(duty);

            var name = UiTheme.Text(m.Name, 13, m.IsOnline ? UiTheme.TextHi : UiTheme.TextDim);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(name);

            var sub = UiTheme.Text(
                $"{ClassName(m.Class)}  Lv {m.Level}", 12, m.IsOnline ? UiTheme.TextLo : UiTheme.TextDim);
            sub.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(sub);

            var seen = UiTheme.Text(m.IsOnline ? "" : LastSeen(m.HoursSinceLogin), 11, UiTheme.TextDim);
            seen.CustomMinimumSize = new Vector2(ClanRankColumn, 0);
            seen.HorizontalAlignment = HorizontalAlignment.Right;
            seen.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(seen);
            _clanList.AddChild(row);
        }
    }

    private static string LastSeen(int hours) =>
        hours < HoursPerDay ? $"{hours}h" : $"{hours / HoursPerDay}d";

    private void OpenMemberMenu(string name)
    {
        bool self = string.Equals(name, Net.I.LastEnter.Name, StringComparison.OrdinalIgnoreCase);
        _ctxMember = name;
        _clanMemberMenu.Clear();
        _clanMemberMenu.AddItem($"— {name} —", 0);
        _clanMemberMenu.SetItemDisabled(0, true);
        if (!self)
        {
            _clanMemberMenu.AddItem("Whisper", 1);
            _clanMemberMenu.AddItem("Invite to party", 2);
            _clanMemberMenu.AddItem("User information", 3);
        }
        if (MyClan.IsChief && !self)
        {
            _clanMemberMenu.AddSeparator();
            _clanMemberMenu.AddItem("Appoint vice-chief", 4);
            _clanMemberMenu.AddItem("Hand over leadership", 5);
            _clanMemberMenu.AddItem("Expel from clan", 6);
        }
        if (_clanMemberMenu.ItemCount <= 1) return;
        _clanMemberMenu.ResetSize();
        _clanMemberMenu.Position = (Vector2I)GetViewport().GetMousePosition();
        _clanMemberMenu.Popup();
    }

    private void OnMemberMenuAction(long id)
    {
        if (_ctxMember.Length == 0) return;
        switch (id)
        {
            case 1: OpenWhisperWith(_ctxMember); break;
            case 2: InvitePlayerToParty(_ctxMember); break;
            case 3: RequestUserInformation(_ctxMember); break;
            case 4: Net.I.SendClanPromoteVice(_ctxMember); break;
            case 5:
            {
                string heir = _ctxMember;
                AskClanConfirm("Hand over leadership", "Hand over",
                    $"Hand the clan over to {heir}? You will no longer be its chief.",
                    () => Net.I.SendClanHandover(heir));
                break;
            }
            case 6:
                _clanRemoveAsk.DialogText = $"Do you really want to expel {_ctxMember}?";
                _clanRemoveAsk.PopupCentered();
                break;
        }
    }

    private static string ClanTabName(ClanTab tab) => tab switch
    {
        ClanTab.Members => "Members",
        ClanTab.Points => "Contribution",
        _ => "Union",
    };

    private void SubmitClanNotice()
    {
        string text = _clanNoticeEdit.Text.Trim();
        if (text.Length == 0) return;
        Net.I.SendClanNotice(text);
        _clanNoticeEdit.Clear();
    }

    private void OnLeaveClan()
    {
        if (!MyClan.InClan) return;
        if (MyClan.IsChief) { _clanDisbandAsk.PopupCentered(); return; }
        _clanLeaveAsk.PopupCentered();
    }

    private void OnClanDonationList(List<(string Name, int Points)> list)
    {
        if (!MyClan.InClan || _clanTab != ClanTab.Points) return;

        foreach (var c in _clanList.GetChildren()) c.QueueFree();
        if (list.Count == 0)
        {
            _clanList.AddChild(UiTheme.Text("Nobody has saved Contribution yet.", 13, UiTheme.TextDim));
            return;
        }

        int rank = 1;
        foreach (var (name, points) in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var pos = UiTheme.Text($"#{rank++}", 12, UiTheme.TextLo);
            pos.CustomMinimumSize = new Vector2(ClanRankColumn, 0);
            hb.AddChild(pos);
            var who = UiTheme.Text(name, 13, UiTheme.TextHi);
            who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(who);
            hb.AddChild(UiTheme.Text($"{points:n0}", 12, UiTheme.Gold));
            _clanList.AddChild(row);
        }
    }

    private void OnClanLeaderPoints(List<(string Name, int Points)> list) => OnClanDonationList(list);

    private void OnClanTop10(List<(int Nation, int Rank, string Name)> top)
    {
        if (!MyClan.InClan) return;
        var target = _clanList;
        foreach (var c in target.GetChildren()) c.QueueFree();
        target.AddChild(UiTheme.Text("Top clans", 13, UiTheme.Gold));
        if (top.Count == 0)
        {
            target.AddChild(UiTheme.Text("No ranked clans yet.", 13, UiTheme.TextDim));
            return;
        }

        foreach (var (nation, rank, name) in top)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var pos = UiTheme.Text($"#{rank + 1}", 12, UiTheme.TextLo);
            pos.CustomMinimumSize = new Vector2(ClanRankColumn, 0);
            hb.AddChild(pos);
            var who = UiTheme.Text(name, 13, UiTheme.TextHi);
            who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(who);
            hb.AddChild(UiTheme.Text(Nations.Name(nation), 12, NationColor(nation)));
            target.AddChild(row);
        }
    }

    private void OnClanCreate(bool ok, int code, string name)
    {
        CloseClanCreate();
        if (ok)
        {
            CombatNotice("You are now a leader of a clan. Congratulations!!!");
            _clanLoaded = true;
            Net.I.SendClanMembersRequest();
            return;
        }

        string text = code switch
        {
            2 => "Sorry. A weakling like you are not fit to become a leader!!",
            3 => "Oh~ I'm sorry, but somebody else is already using that name. Try a different name.",
            4 => $"Sorry. You need {ClanTypes.CreationCoins:n0} gold in order to create a clan.",
            5 => "You can't create a clan because you're already in another clan.",
            7 => "You cannot create a clan today.",
            8 => "Creating a clan is only allowed in the 1st server group.",
            9 => "Creating a clan is only allowed in your nation's village or Moradon.",
            _ => "Hm.. You can't create a clan right now. Please come back later.",
        };
        CombatNotice(text);
        if (code == 3) OpenClanCreate(text);
    }

    private void OnClanInvite(int inviterId, int clanId, string clanName)
    {
        _clanInviterId = inviterId;
        _clanInviteClanId = clanId;
        _clanInviteAsk.DialogText = $"Will you join the clan {clanName} ?";
        _clanInviteAsk.PopupCentered();
    }

    private void AnswerClanInvite(bool accept)
    {
        if (_clanInviterId == 0) return;
        Net.I.SendClanInviteAnswer(accept, _clanInviterId, _clanInviteClanId);
        _clanInviterId = 0;
        _clanInviteClanId = 0;
    }

    private void OnClanResult(int sub, int code)
    {
        bool ok = code == Net.KnResultOk;
        switch (sub)
        {
            case Net.KnJoin:
                if (ok) { CombatNotice(ClanMsgJoined); _clanLoaded = true; Net.I.SendClanMembersRequest(); }
                else CombatNotice(ClanRefusal(code));
                return;
            case Net.KnWithdraw:
                CombatNotice(ok ? ClanMsgQuit : ClanMsgQuitFailed);
                if (!ok) CombatNotice(code == 12 ? ClanMsgZone : ClanMsgQuitFailed);
                return;
            case Net.KnDestroy:
                if (ok) CombatNotice("The clan has been disbanded.");
                else CombatNotice(ClanRefusal(code));
                return;
            case Net.KnRemove:
                if (ok) Net.I.SendClanMembersRequest();
                else CombatNotice(ClanRefusal(code));
                return;
            case Net.KnReject:
                CombatNotice(ClanMsgDeclined);
                return;
            case Net.KnAllyCreate:
            case Net.KnAllyReq:
            case Net.KnAllyInsert:
            case Net.KnAllyRemove:
            case Net.KnAllyPunish:
                CombatNotice(ClanMsgAllianceFailed);
                return;
            default:
                if (ok) Net.I.SendClanMembersRequest();
                else CombatNotice(ClanRefusal(code));
                return;
        }
    }

    private static string ClanRefusal(int code) => code switch
    {
        2 => ClanMsgNoUser,
        3 => ClanMsgDead,
        4 => ClanMsgOtherNation,
        5 => ClanMsgAlreadyInClan,
        6 => ClanMsgNoAuthority,
        7 => ClanMsgInvalid,
        8 => ClanMsgFull,
        9 => ClanMsgYourself,
        10 => ClanMsgNotInClan,
        11 => ClanMsgDeclined,
        12 => ClanMsgZone,
        16 => ClanMsgBanned,
        _ => ClanMsgCommandUnavailable,
    };

    private void OnClanNoticeRefused(int code) => CombatNotice(code switch
    {
        1 => ClanMsgNoticeAuthority,
        3 => ClanMsgNoticeName,
        _ => ClanMsgCommandUnavailable,
    });

    private void OnClanHandoverList(bool leader, List<string> names)
    {
        if (!leader) { CombatNotice(ClanMsgHandoverAuthority); return; }
        if (names.Count == 0) CombatNotice(ClanMsgHandoverNoVice);
    }

    private void OnClanHandover(bool ok, string oldChief, string newChief)
    {
        if (!ok) { CombatNotice(ClanMsgHandoverFailed); return; }
        CombatNotice(string.Equals(oldChief, Net.I.LastEnter.Name, StringComparison.OrdinalIgnoreCase)
            ? $"You have handed over leadership to {newChief}."
            : $"{oldChief}, the leader of {MyClan.Name}, passes leadership to {newChief}.");
        Net.I.SendClanMembersRequest();
    }

    private void OnRemovedFromClan() => CombatNotice(ClanMsgBanned);

    private void OnClanFame(int charId, int clanId, byte fame)
    {
        if (charId == _myId)
        {
            if (clanId == 0) return;
            CombatNotice($"You are now {ClanRanks.Name(fame)} of the clan.");
            if (ClanPageVisible) Net.I.SendClanMembersRequest();
            return;
        }

        if (clanId == 0) { OnEntityClanCleared(charId); return; }
        for (int i = 0; i < _clanMembers.Count; i++)
        {
            if (!_ents.TryGetValue(charId, out var ent) || !string.Equals(_clanMembers[i].Name, ent.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            var member = _clanMembers[i];
            member.Fame = fame;
            _clanMembers[i] = member;
        }
        if (ClanPageVisible && _clanTab == ClanTab.Members) RenderClanMembers();
    }

    private void OnClanMemberPresence(string name, bool online)
    {
        CombatNotice(online ? $"{name} is online." : $"{name} is offline.");
        for (int i = 0; i < _clanMembers.Count; i++)
        {
            if (!string.Equals(_clanMembers[i].Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            var member = _clanMembers[i];
            member.IsOnline = online;
            member.HoursSinceLogin = 0;
            _clanMembers[i] = member;
        }
        if (ClanPageVisible && _clanTab == ClanTab.Members) RenderClanMembers();
    }

    private void OnClanStanding(List<ClanStanding> standings)
    {
        foreach (var standing in standings)
        {
            foreach (var (id, ent) in _ents)
            {
                if (ent.IsNpc || ent.KnightsId != standing.ClanId) continue;
                ent.ClanGrade = standing.Grade;
                ent.ClanRanking = standing.Ranking;
                AttachClanGauntlet(ent.Body, ent.Race, standing.Grade, standing.Ranking);
            }
        }
        if (ClanPageVisible) ApplyMyClan();
    }

    private void OnEntityClan(int charId, int clanId, string name, byte grade, byte ranking, byte fame, int capeId, int colour)
    {
        if (charId == _myId || !_ents.TryGetValue(charId, out var ent)) return;
        ent.KnightsId = clanId;
        ent.ClanGrade = grade;
        ent.ClanRanking = ranking;
        ent.Plate?.SetClan(name);
        if (capeId >= 0)
        {
            ent.CapeId = capeId; ent.CapeR = colour & 0xFF; ent.CapeG = (colour >> 8) & 0xFF; ent.CapeB = (colour >> 16) & 0xFF;
            DressCape(ent.Body, ent.CapeId, ent.CapeR, ent.CapeG, ent.CapeB, ent.IsGm, ent.Race);
        }
        AttachClanGauntlet(ent.Body, ent.Race, grade, ranking);
        if (MyClan.InClan && clanId == MyClan.ClanId && ClanPageVisible) Net.I.SendClanMembersRequest();
    }

    private void OnEntityClanCleared(int charId)
    {
        if (charId == _myId || !_ents.TryGetValue(charId, out var ent)) return;
        bool wasMine = MyClan.InClan && ent.KnightsId == MyClan.ClanId;
        ent.KnightsId = 0;
        ent.ClanGrade = 0;
        ent.ClanRanking = 0;
        ent.CapeId = 0;
        ent.Plate?.SetClan("");
        DressCape(ent.Body, 0, 0, 0, 0, ent.IsGm, ent.Race);
        AttachClanGauntlet(ent.Body, ent.Race, 0, 0);
        if (wasMine && ClanPageVisible) Net.I.SendClanMembersRequest();
    }

    private void AskClanConfirm(string title, string ok, string text, Action confirmed)
    {
        _clanConfirmed = confirmed;
        _clanConfirmAsk.Title = title;
        _clanConfirmAsk.GetOkButton().Text = ok;
        _clanConfirmAsk.DialogText = text;
        _clanConfirmAsk.PopupCentered();
    }

    private void OnAllianceButton()
    {
        if (!MyClan.IsChief) return;
        if (MyClan.AllianceId > 0)
        {
            AskClanConfirm("Leave alliance", "Leave",
                MyClan.AllianceId == MyClan.ClanId
                    ? "Your clan leads this alliance. Leave it anyway?"
                    : "Leave the alliance?",
                Net.I.SendAllianceLeave);
            return;
        }

        if (_selectedId <= 0 || !_ents.TryGetValue(_selectedId, out var target) || target.IsNpc)
        {
            CombatNotice("Target the chief of the clan you want to ally with.");
            return;
        }

        if (MyClan.AllianceId == MyClan.ClanId) Net.I.SendAllianceInsert(_selectedId);
        else Net.I.SendAllianceCreate(_selectedId);
        SetClanStatus($"Alliance request sent to {target.Name}.", false);
    }

    private void OnAllianceInvite(string clanName, int clanId)
    {
        _clanAllianceAsk.DialogText = $"The Knights {clanName} has sent a request for confederacy.  Will you accept?";
        _clanAllianceAsk.PopupCentered();
    }

    private void OnAllianceMembership(int allianceId, int clanId, bool joined)
    {
        ApplyMyClan();
        if (ClanPageVisible && _clanTab == ClanTab.Union) Net.I.SendAllianceList();
    }

    private void OnAllianceList(string notice, List<AllianceClanEntry> clans)
    {
        _allianceNotice = notice;
        _allianceClans = clans;
        if (!MyClan.InClan || _clanTab != ClanTab.Union) return;

        foreach (var c in _clanList.GetChildren()) c.QueueFree();
        if (clans.Count == 0)
        {
            SetClanListHint(MyClan.IsChief && MyClan.Flag >= ClanTypes.Promoted
                ? "Your Knights are in no alliance. Target another chief and press Ally with target."
                : "Your Knights are in no alliance.");
            return;
        }

        SetClanListHint(notice.Length > 0 ? notice : "Alliance");
        foreach (var clan in clans)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var col = new VBoxContainer(); col.AddThemeConstantOverride("separation", 0);
            row.AddChild(col);
            var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 8);
            col.AddChild(head);
            var name = UiTheme.Text(clan.Name, 13, clan.Id == MyClan.ClanId ? UiTheme.GoldBright : UiTheme.TextHi);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            head.AddChild(name);
            head.AddChild(UiTheme.Text(clan.Id == MyClan.AllianceId ? "leads" : "member", 11, UiTheme.TextLo));
            if (MyClan.IsChief && MyClan.AllianceId == MyClan.ClanId && clan.Id != MyClan.ClanId)
            {
                int punishId = clan.Id;
                string punishName = clan.Name;
                var kick = new Button { Text = "Expel", FocusMode = Control.FocusModeEnum.None };
                kick.Pressed += () => AskClanConfirm("Expel from alliance", "Expel",
                    $"Expel {punishName} from the alliance?", () => Net.I.SendAlliancePunish(punishId));
                head.AddChild(kick);
            }
            var officers = new List<string>();
            foreach (var officer in clan.Officers) officers.Add($"{ClanRanks.Name(officer.Fame)} {officer.Name}");
            var line = UiTheme.Text(string.Join("   ·   ", officers), 11, UiTheme.TextLo);
            line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            line.CustomMinimumSize = new Vector2(1, 0);
            col.AddChild(line);
            _clanList.AddChild(row);
        }
    }

    private void SetClanListHint(string text)
    {
        _clanListHint.Text = text;
        _clanListHint.Visible = text.Length > 0;
    }

    private void SetClanStatus(string text, bool warn)
    {
        _clanStatus.Text = text;
        _clanStatus.AddThemeColorOverride("font_color", warn ? UiTheme.Bad : UiTheme.TextLo);
    }

    private sealed partial class ClanMemberRow : PanelContainer
    {
        private readonly System.Action _onRightClick;
        public ClanMemberRow(System.Action onRightClick) => _onRightClick = onRightClick;

        public override void _GuiInput(InputEvent ev)
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
                _onRightClick();
        }
    }
}
