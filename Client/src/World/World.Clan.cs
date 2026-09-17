using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int ClanPanelWidth = 500;

    private CanvasLayer _clanLayer = null!;
    private HudWindow _clanPanel = null!;
    private Label _clanStatus = null!;
    private Label _clanNameLbl = null!, _clanStandingLbl = null!;
    private Label _clanPointsLbl = null!, _clanNoticeLbl = null!, _clanMemberCountLbl = null!;
    private LineEdit _clanCreateName = null!, _clanNoticeEdit = null!;
    private VBoxContainer _clanMemberList = null!, _clanBrowseList = null!;
    private VBoxContainer _clanMineBox = null!, _clanJoinBox = null!;
    private HBoxContainer _clanNoticeRow = null!, _clanDonateRow = null!;
    private Button _clanLeaveBtn = null!, _clanNoticeBtn = null!;

    internal enum ClanTab { Members, Donations, TopClans }

    private readonly Dictionary<ClanTab, Button> _clanTabButtons = new();
    private ClanTab _clanTab = ClanTab.Members;
    private ConfirmationDialog _clanJoinAsk = null!;
    private ConfirmationDialog _clanDisbandAsk = null!;
    private PopupMenu _clanMemberMenu = null!;
    private LineEdit _clanDonateEdit = null!;
    private bool _clanShown;

    private MyClanInfo _myClan;
    private string _joinReqName = "";
    private string _ctxMember = "";

    private bool ImChief => _myClan.InClan && string.Equals(_myClan.Chief, Net.I.LastEnter.Name, System.StringComparison.OrdinalIgnoreCase);

    private void ClanInit()
    {
        BuildClanPanel();
        Net.I.ClanListEvent += OnClanList;
        Net.I.MyClanInfoEvent += OnMyClanInfo;
        Net.I.ClanMembersEvent += OnClanMembers;
        Net.I.ClanCreateEvent += OnClanCreate;
        Net.I.ClanJoinedEvent += OnClanJoined;
        Net.I.ClanJoinRequestEvent += OnClanJoinRequest;
        Net.I.ClanResultEvent += OnClanResult;
        Net.I.ClanNoticeEvent += OnClanNotice;
        Net.I.ClanNoticeRefusedEvent += OnClanNoticeRefused;
        Net.I.ClanDonateEvent += OnClanDonate;
        Net.I.ClanTop10Event += OnClanTop10;
        Net.I.ClanDonationListEvent += OnClanDonationList;
        Net.I.SendClanInfoRequest();
    }

    private void ClanDispose()
    {
        Net.I.ClanListEvent -= OnClanList;
        Net.I.MyClanInfoEvent -= OnMyClanInfo;
        Net.I.ClanMembersEvent -= OnClanMembers;
        Net.I.ClanCreateEvent -= OnClanCreate;
        Net.I.ClanJoinedEvent -= OnClanJoined;
        Net.I.ClanJoinRequestEvent -= OnClanJoinRequest;
        Net.I.ClanResultEvent -= OnClanResult;
        Net.I.ClanNoticeEvent -= OnClanNotice;
        Net.I.ClanNoticeRefusedEvent -= OnClanNoticeRefused;
        Net.I.ClanDonateEvent -= OnClanDonate;
        Net.I.ClanTop10Event -= OnClanTop10;
        Net.I.ClanDonationListEvent -= OnClanDonationList;
    }

    private void BuildClanPanel()
    {
        _clanLayer = new CanvasLayer { Layer = 74 };
        AddChild(_clanLayer);

        _clanPanel = new HudWindow("clan", "Clan", new Vector2(150, 80), bodyMinWidth: ClanPanelWidth)
        { Visible = false };
        _clanPanel.Closed += CloseClan;
        _clanLayer.AddChild(_clanPanel);
        var r = _clanPanel.Body;
        r.AddThemeConstantOverride("separation", 8);

        BuildClanMineView(r);
        BuildClanJoinView(r);

        _clanStatus = HudStyle.Label(13);
        r.AddChild(_clanStatus);

        _clanMemberMenu = new PopupMenu();
        _clanMemberMenu.IdPressed += OnMemberMenuAction;
        _clanLayer.AddChild(_clanMemberMenu);

        _clanJoinAsk = new ConfirmationDialog { Title = "Join request" };
        _clanJoinAsk.GetOkButton().Text = "Admit";
        _clanJoinAsk.GetCancelButton().Text = "Decline";
        _clanJoinAsk.Confirmed += () => { if (_joinReqName.Length > 0) Net.I.SendClanAdmit(_joinReqName); };
        _clanJoinAsk.Canceled += () => { if (_joinReqName.Length > 0) Net.I.SendClanReject(_joinReqName); };
        _clanLayer.AddChild(_clanJoinAsk);

        _clanDisbandAsk = new ConfirmationDialog
        {
            Title = "Disband clan",
            DialogText = "You are the clan leader, so leaving disbands the clan.\nContinue?",
        };
        _clanDisbandAsk.GetOkButton().Text = "Disband";
        _clanDisbandAsk.Confirmed += () => Net.I.SendClanDestroy();
        _clanLayer.AddChild(_clanDisbandAsk);
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
        _clanPointsLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        cardCol.AddChild(_clanPointsLbl);

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
        };
        _clanNoticeRow.AddChild(_clanNoticeEdit);
        _clanNoticeBtn = new Button { Text = "Set notice", FocusMode = Control.FocusModeEnum.None };
        _clanNoticeBtn.Pressed += () =>
        {
            if (_clanNoticeEdit.Text.Trim().Length > 0) Net.I.SendClanNotice(_clanNoticeEdit.Text.Trim());
        };
        _clanNoticeRow.AddChild(_clanNoticeBtn);
        _clanMineBox.AddChild(_clanNoticeRow);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 2);
        _clanMineBox.AddChild(tabs);
        foreach (var tab in new[] { ClanTab.Members, ClanTab.Donations, ClanTab.TopClans })
        {
            var which = tab;
            var button = UiTheme.TopTabButton(ClanTabName(tab), 12);
            button.Pressed += () => ShowClanTab(which);
            _clanTabButtons[tab] = button;
            tabs.AddChild(button);
        }

        var listHead = new HBoxContainer();
        listHead.AddThemeConstantOverride("separation", 6);
        listHead.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _clanMemberCountLbl = UiTheme.Text("", 12, UiTheme.TextLo);
        listHead.AddChild(_clanMemberCountLbl);
        _clanMineBox.AddChild(listHead);

        _clanMemberList = ClanScroll(_clanMineBox, 190);

        _clanDonateRow = new HBoxContainer { Visible = false };
        _clanDonateRow.AddThemeConstantOverride("separation", 6);
        _clanMineBox.AddChild(_clanDonateRow);
        var donate = _clanDonateRow;

        var donateLbl = HudStyle.Label(13);
        donateLbl.Text = "Donate NP";
        donate.AddChild(donateLbl);
        _clanDonateEdit = new LineEdit
        {
            PlaceholderText = "amount",
            Alignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(110, 0),
        };
        donate.AddChild(_clanDonateEdit);
        var donateBtn = new Button { Text = "Donate", FocusMode = Control.FocusModeEnum.None };
        donateBtn.Pressed += () =>
        {
            if (int.TryParse(_clanDonateEdit.Text.Trim(), out int a) && a > 0) Net.I.SendClanDonate(a);
        };
        donate.AddChild(donateBtn);
        donate.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        _clanMineBox.AddChild(actions);

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

        var intro = UiTheme.Text(
            "You are not in a clan. Start one, or ask to join one below.", 13, UiTheme.TextLo);
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.CustomMinimumSize = new Vector2(1, 0);
        _clanJoinBox.AddChild(intro);

        var createRow = new HBoxContainer();
        createRow.AddThemeConstantOverride("separation", 6);
        _clanJoinBox.AddChild(createRow);
        _clanCreateName = new LineEdit
        {
            PlaceholderText = "new clan name",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        createRow.AddChild(_clanCreateName);
        var createBtn = new Button { Text = "Create", FocusMode = Control.FocusModeEnum.None };
        createBtn.Pressed += () =>
        {
            string n = _clanCreateName.Text.Trim();
            if (n.Length >= 2) Net.I.SendClanCreate(n);
            else SetClanStatus("Name must be 2-20 chars.", true);
        };
        createRow.AddChild(createBtn);

        var browseRow = new HBoxContainer();
        browseRow.AddThemeConstantOverride("separation", 6);
        _clanJoinBox.AddChild(browseRow);
        browseRow.AddChild(UiTheme.SectionTitle("Clans"));
        browseRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var refreshBtn = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += () => Net.I.SendClanList();
        browseRow.AddChild(refreshBtn);
        var topBtn = new Button { Text = "Top 10", FocusMode = Control.FocusModeEnum.None };
        topBtn.Pressed += () => Net.I.SendClanTop10();
        browseRow.AddChild(topBtn);

        _clanBrowseList = ClanScroll(_clanJoinBox, 240);
    }

    private static VBoxContainer ClanScroll(VBoxContainer parent, int height)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(1, height), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return list;
    }

    private void ToggleClan()
    {
        if (_clanShown) { CloseClan(); return; }
        _clanPanel.Visible = true;
        _clanShown = true;
        SetClanStatus("", false);
        Net.I.SendClanInfoRequest();
        Net.I.SendClanMembersRequest();
        Net.I.SendClanList();
    }

    private void CloseClan()
    {
        if (!_clanShown) return;
        _clanShown = false;
        _clanPanel.Visible = false;
    }

    private void OnLeaveClan()
    {
        if (!_myClan.InClan) { SetClanStatus("You're not in a clan.", true); return; }

        if (ImChief)
        {
            _clanDisbandAsk.PopupCentered();
            return;
        }

        Net.I.SendClanWithdraw();
    }

    private void OnMyClanInfo(MyClanInfo info)
    {
        _myClan = info;
        ApplySelfClan(info.InClan ? info.Name : "");

        _clanMineBox.Visible = info.InClan;
        _clanJoinBox.Visible = !info.InClan;
        if (info.InClan && _clanTabButtons.Count > 0 && !_clanTabButtons[ClanTab.Members].ButtonPressed)
            _clanTabButtons[ClanTab.Members].ButtonPressed = true;
        if (_self != null)
            AttachClanGauntlet(_self, Net.I.LastEnter.Race, info.InClan ? info.Grade : 0);

        if (!info.InClan)
        {
            foreach (var c in _clanMemberList.GetChildren()) c.QueueFree();
            Net.I.SendClanList();
            return;
        }

        _clanNameLbl.Text = info.Name;
        _clanStandingLbl.Text =
            $"{ClanTypeName(info.Flag)}   ·   Grade {ClanGradeName(info.Grade)}"
            + $"   ·   {info.Members} member{(info.Members == 1 ? "" : "s")}";
        _clanPointsLbl.Text = $"Clan points {info.Points:n0}   ·   Fund {info.PointFund:n0}";

        string notice = info.Notice ?? "";
        bool hasNotice = notice.Length > 0;
        _clanNoticeLbl.Text = hasNotice ? notice : "No notice set.";
        _clanNoticeLbl.AddThemeColorOverride("font_color", hasNotice ? UiTheme.Gold : UiTheme.TextDim);
        _clanNoticeRow.Visible = ImChief;
        _clanLeaveBtn.Text = ImChief ? "Disband clan" : "Leave clan";
        _clanLeaveBtn.Disabled = false;
        _clanNoticeBtn.Disabled = false;
    }

    private void OnClanMembers(List<ClanMember> members)
    {
        if (_clanTab != ClanTab.Members) return;
        foreach (var c in _clanMemberList.GetChildren()) c.QueueFree();
        if (members.Count == 0)
        {
            _clanMemberCountLbl.Text = "";
            var e = UiTheme.Text("No members.", 13, UiTheme.TextDim);
            _clanMemberList.AddChild(e);
            return;
        }
        int online = 0;
        foreach (var m in members) if (m.IsOnline) online++;
        _clanMemberCountLbl.Text = $"{online} of {members.Count} online";

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
            var row = new ClanMemberRow(() => OpenMemberMenu(mName));
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);

            var dot = UiTheme.Text("•", 15, m.IsOnline ? UiTheme.Good : UiTheme.TextDim);
            dot.CustomMinimumSize = new Vector2(10, 0);
            dot.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(dot);

            var rank = UiTheme.Text(ClanRank(m.Fame), 12, m.Fame == 1 ? UiTheme.Gold : UiTheme.TextLo);
            rank.CustomMinimumSize = new Vector2(58, 0);
            rank.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(rank);

            var name = UiTheme.Text(m.Name, 13, m.IsOnline ? UiTheme.TextHi : UiTheme.TextDim);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(name);

            var sub = UiTheme.Text(
                $"{ClassName(m.Class)}  Lv {m.Level}", 12, m.IsOnline ? UiTheme.TextLo : UiTheme.TextDim);
            sub.MouseFilter = Control.MouseFilterEnum.Ignore;
            hb.AddChild(sub);
            _clanMemberList.AddChild(row);
        }
    }

    private void OpenMemberMenu(string name)
    {
        if (!ImChief) return;
        if (string.Equals(name, Net.I.LastEnter.Name, System.StringComparison.OrdinalIgnoreCase)) return;
        _ctxMember = name;
        _clanMemberMenu.Clear();
        _clanMemberMenu.AddItem($"— {name} —", 0);
        _clanMemberMenu.SetItemDisabled(0, true);
        _clanMemberMenu.AddItem("Promote to Officer", 1);
        _clanMemberMenu.AddItem("Promote to Vice-Chief", 2);
        _clanMemberMenu.AddItem("Hand over Chief", 3);
        _clanMemberMenu.AddSeparator();
        _clanMemberMenu.AddItem("Kick from clan", 4);
        _clanMemberMenu.Position = (Vector2I)GetViewport().GetMousePosition();
        _clanMemberMenu.Popup();
    }

    private void OnMemberMenuAction(long id)
    {
        if (_ctxMember.Length == 0) return;
        switch (id)
        {
            case 1: Net.I.SendClanPromoteOfficer(_ctxMember); break;
            case 2: Net.I.SendClanPromoteVice(_ctxMember); break;
            case 3: Net.I.SendClanPromoteChief(_ctxMember); break;
            case 4: Net.I.SendClanKick(_ctxMember); break;
        }
    }

    private static string ClanTabName(ClanTab tab) => tab switch
    {
        ClanTab.Members => "Members",
        ClanTab.Donations => "Donations",
        _ => "Top clans",
    };

    private void ShowClanTab(ClanTab tab)
    {
        _clanTab = tab;
        foreach (var (key, button) in _clanTabButtons) button.ButtonPressed = key == tab;

        _clanMemberCountLbl.Text = "";
        _clanDonateRow.Visible = tab == ClanTab.Donations;
        foreach (var c in _clanMemberList.GetChildren()) c.QueueFree();
        _clanMemberList.AddChild(UiTheme.Text("Loading…", 12, UiTheme.TextDim));

        switch (tab)
        {
            case ClanTab.Members: Net.I.SendClanMembersRequest(); break;
            case ClanTab.Donations: Net.I.SendClanDonationList(); break;
            default: Net.I.SendClanTop10(); break;
        }
    }

    private void OnClanDonationList(List<(string Name, int Loyalty)> list)
    {
        if (!_myClan.InClan) { RenderBrowseDonations(list); return; }
        if (_clanTab != ClanTab.Donations) return;

        foreach (var c in _clanMemberList.GetChildren()) c.QueueFree();
        _clanMemberCountLbl.Text = "national points given to the clan";
        if (list.Count == 0)
        {
            _clanMemberList.AddChild(UiTheme.Text("Nobody has donated yet.", 13, UiTheme.TextDim));
            return;
        }

        int rank = 1;
        foreach (var (name, loyalty) in list)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var pos = UiTheme.Text($"#{rank++}", 12, UiTheme.TextLo);
            pos.CustomMinimumSize = new Vector2(34, 0);
            hb.AddChild(pos);
            var who = UiTheme.Text(name, 13, UiTheme.TextHi);
            who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(who);
            hb.AddChild(UiTheme.Text($"{loyalty:n0}", 12, UiTheme.Gold));
            _clanMemberList.AddChild(row);
        }
    }

    private void RenderBrowseDonations(List<(string Name, int Loyalty)> list)
    {
        foreach (var c in _clanBrowseList.GetChildren()) c.QueueFree();
        _clanBrowseList.AddChild(UiTheme.Text("Clan donations", 13, UiTheme.Gold));
        if (list.Count == 0)
        {
            _clanBrowseList.AddChild(UiTheme.Text("No donations recorded.", 13, UiTheme.TextDim));
            return;
        }
        int rank = 1;
        foreach (var (name, loyalty) in list)
            _clanBrowseList.AddChild(UiTheme.Text($"#{rank++}  {name}  —  {loyalty:n0}", 13, UiTheme.TextHi));
    }

    private void OnClanDonate(bool ok, int loyalty)
    {
        SetClanStatus(ok ? "Donated to the clan fund." : "Donation failed (not enough NP).", !ok);
        if (ok) _clanDonateEdit.Text = "";
    }

    private void OnClanTop10(List<(int Nation, int Rank, string Name)> top)
    {
        if (!_myClan.InClan) { RenderBrowseTop(top); return; }
        if (_clanTab != ClanTab.TopClans) return;

        foreach (var c in _clanMemberList.GetChildren()) c.QueueFree();
        _clanMemberCountLbl.Text = "highest ranked clans on the server";
        if (top.Count == 0)
        {
            _clanMemberList.AddChild(UiTheme.Text("No ranked clans yet.", 13, UiTheme.TextDim));
            return;
        }

        foreach (var (nation, rank, name) in top)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var pos = UiTheme.Text($"#{rank + 1}", 12, UiTheme.TextLo);
            pos.CustomMinimumSize = new Vector2(34, 0);
            hb.AddChild(pos);
            var who = UiTheme.Text(name, 13, UiTheme.TextHi);
            who.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hb.AddChild(who);
            hb.AddChild(UiTheme.Text(Nations.Name(nation), 12,
                nation == Nations.Karus ? new Color("d08a4a") : new Color("5a9ad0")));
            _clanMemberList.AddChild(row);
        }
    }

    private void RenderBrowseTop(List<(int Nation, int Rank, string Name)> top)
    {
        foreach (var c in _clanBrowseList.GetChildren()) c.QueueFree();
        _clanBrowseList.AddChild(UiTheme.Text("Top clans", 13, UiTheme.Gold));
        if (top.Count == 0)
        {
            _clanBrowseList.AddChild(UiTheme.Text("No ranked clans yet.", 13, UiTheme.TextDim));
            return;
        }
        foreach (var (nation, rank, name) in top)
            _clanBrowseList.AddChild(UiTheme.Text($"#{rank + 1}  [{Nations.Name(nation)}]  {name}", 13,
                nation == Nations.Karus ? new Color("d08a4a") : new Color("5a9ad0")));
    }

    private void OnClanList(List<ClanBrowseEntry> clans)
    {
        foreach (var c in _clanBrowseList.GetChildren()) c.QueueFree();
        if (clans.Count == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No clans found.";
            _clanBrowseList.AddChild(e);
            return;
        }
        foreach (var clan in clans)
        {
            int id = clan.Id;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", -2);
            var name = UiTheme.Text(clan.Name, 13, UiTheme.TextHi);
            info.AddChild(name);
            var meta = UiTheme.Text($"{ClanTypeName(clan.Flag)}   ·   Chief {clan.Chief}   ·   {clan.Members} members   ·   {clan.Points:n0} pts", 11, UiTheme.TextLo);
            info.AddChild(meta);
            hb.AddChild(info);
            var joinBtn = new Button { Text = "Join", FocusMode = Control.FocusModeEnum.None };
            joinBtn.Disabled = _myClan.InClan;
            string clanName = clan.Name;
            joinBtn.Pressed += () =>
            {
                Net.I.SendClanJoinRequest(id);
                SetClanStatus($"Applied to {clanName}. The chief has to admit you.", false);
            };
            hb.AddChild(joinBtn);
            _clanBrowseList.AddChild(row);
        }
    }

    private void OnClanCreate(bool ok, int code, string name)
    {
        if (ok)
        {
            SetClanStatus($"Clan '{name}' founded!", false);
            Net.I.SendClanInfoRequest();
            Net.I.SendClanMembersRequest();
        }
        else SetClanStatus(code switch
        {
            2 => "You must be level 30 to found a clan.",
            3 => "That clan name is taken or invalid.",
            4 => "Founding a clan costs 500,000 gold.",
            5 => "You're already in a clan.",
            _ => "Couldn't create the clan.",
        }, true);
    }

    private void OnClanJoined(int clanId, string name)
    {
        Chat.Info($"You joined the clan {name}.");
        Net.I.SendClanInfoRequest();
        Net.I.SendClanMembersRequest();
    }

    private void OnClanJoinRequest(int charId, string name)
    {
        _joinReqName = name;
        _clanJoinAsk.DialogText = $"{name} wants to join your clan.\nAdmit?";
        _clanJoinAsk.PopupCentered();
        Chat.Info($"{name} wants to join your clan. (X to manage)");
    }

    private void OnClanResult(int sub, int code)
    {
        bool ok = code == ClanResultOk;
        if (!ok)
        {
            SetClanStatus(ClanRefusal(sub, code), true);
            return;
        }

        switch (sub)
        {
            case 0x02:
                break;
            case 0x03:
                Chat.Info("You left the clan.");
                Net.I.SendClanInfoRequest();
                Net.I.SendClanMembersRequest();
                break;
            case 0x05:
                Chat.Info("Your clan was disbanded.");
                Net.I.SendClanInfoRequest();
                Net.I.SendClanMembersRequest();
                break;
            case 0x04:
                Chat.Info("Member removed.");
                Net.I.SendClanMembersRequest();
                break;
            default:
                Net.I.SendClanMembersRequest();
                break;
        }
    }

    private static string ClanRefusal(int sub, int code) => code switch
    {
        2 => "No character by that name.",
        3 => "That character is dead.",
        4 => "That character belongs to the other nation.",
        5 => "Already in a clan.",
        6 => sub == 0x02
            ? "That clan's chief isn't online to admit you."
            : "You don't have the authority for that.",
        7 => "That clan no longer exists.",
        8 => "That clan is full.",
        9 => "You can't pick yourself.",
        10 => "Not a member of that clan.",
        11 => "They declined.",
        12 => "Not allowed in this zone.",
        _ => "That clan action was refused.",
    };

    private void OnClanNotice(string notice)
    {
        if (notice.Length > 0) Chat.Info($"[Clan] {notice}");
    }

    private void OnClanNoticeRefused(int code) => SetClanStatus(code switch
    {
        1 => "Only the clan chief can set the notice.",
        3 => "That character name was not recognised.",
        _ => "The notice could not be set right now.",
    }, true);

    private const int ClanResultOk = 1;

    private void SetClanStatus(string text, bool warn)
    {
        _clanStatus.Text = text;
        _clanStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }

    private static string ClanRank(byte fame) => fame switch
    {
        1 => "Chief", 2 => "Vice", 3 => "Officer", _ => "Member",
    };

    private static string ClanTypeName(byte flag) => flag switch
    {
        >= 3 => "Accredited", 2 => "Promoted", _ => "Training",
    };

    private static string ClanGradeName(byte grade) => grade is >= 1 and <= 5 ? grade.ToString() : "-";

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
