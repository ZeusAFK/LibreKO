using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _partyLayer = null!;
    private VBoxContainer _partyContent = null!;
    private Label _partyHeaderLbl = null!;
    private VBoxContainer _partyMembersBox = null!;
    private Button _partyDisbandBtn = null!;

    private PopupMenu _partyCtxMenu = null!;
    private int _ctxMemberId = -1;

    private PanelContainer _inviteNamePanel = null!;
    private LineEdit _inviteNameInput = null!;
    private ConfirmationDialog _inviteAskDialog = null!;
    private CanvasLayer _partyDialogLayer = null!;
    private bool _invitePending;

    private const int PartyMaxMembers = 8;

    private static IReadOnlyList<PartyMember> PartyMembers => Net.I.Party;
    private bool InParty => Net.I.InParty;
    private bool AmLeader => PartyMembers.Count > 0 && PartyMembers[0].CharId == _myId;

    private void PartyInit()
    {
        BuildPartyPanel();
        BuildSeekPartyPanel();

        Net.I.PartyMemberEvent += OnPartyMember;
        Net.I.PartyErrorEvent += OnPartyError;
        Net.I.PartyInviteEvent += OnPartyInvite;
        Net.I.PartyRemovedEvent += OnPartyRemoved;
        Net.I.PartyDisbandEvent += OnPartyDisband;
        Net.I.PartyMemberStatsEvent += OnPartyMemberStats;
        Net.I.PartyMemberLevelEvent += OnPartyMemberLevel;
        Net.I.PartyMemberClassEvent += OnPartyMemberClass;
        Net.I.PartyMemberStatusEvent += OnPartyMemberStatus;
        Net.I.PartyBbsRegisterEvent += OnBbsRegister;
        Net.I.PartyBbsDeleteEvent += OnBbsDelete;
        Net.I.PartyBbsListEvent += OnBbsList;
        Net.I.PartyBbsWantedFailEvent += OnBbsWantedFail;
        Net.I.SelfHpEvent += OnPartySelfHp;
        Net.I.SelfMpEvent += OnPartySelfMp;
    }

    private void PartyDispose()
    {
        Net.I.PartyMemberEvent -= OnPartyMember;
        Net.I.PartyErrorEvent -= OnPartyError;
        Net.I.PartyInviteEvent -= OnPartyInvite;
        Net.I.PartyRemovedEvent -= OnPartyRemoved;
        Net.I.PartyDisbandEvent -= OnPartyDisband;
        Net.I.PartyMemberStatsEvent -= OnPartyMemberStats;
        Net.I.PartyMemberLevelEvent -= OnPartyMemberLevel;
        Net.I.PartyMemberClassEvent -= OnPartyMemberClass;
        Net.I.PartyMemberStatusEvent -= OnPartyMemberStatus;
        Net.I.PartyBbsRegisterEvent -= OnBbsRegister;
        Net.I.PartyBbsDeleteEvent -= OnBbsDelete;
        Net.I.PartyBbsListEvent -= OnBbsList;
        Net.I.PartyBbsWantedFailEvent -= OnBbsWantedFail;
        Net.I.SelfHpEvent -= OnPartySelfHp;
        Net.I.SelfMpEvent -= OnPartySelfMp;
    }

    private void BuildPartyPanel()
    {
        _partyLayer = new CanvasLayer { Layer = 70 };
        AddChild(_partyLayer);

        _partyContent = new VBoxContainer { CustomMinimumSize = new Vector2(252, 0) };
        _partyContent.AddThemeConstantOverride("separation", 6);

        _partyHeaderLbl = HudStyle.Label(15);
        _partyContent.AddChild(_partyHeaderLbl);

        _partyMembersBox = new VBoxContainer();
        _partyMembersBox.AddThemeConstantOverride("separation", 7);
        _partyContent.AddChild(_partyMembersBox);

        _partyContent.AddChild(new HSeparator());

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 5);
        _partyContent.AddChild(btnRow);

        var inviteBtn = SmallButton("Invite", btnRow);
        inviteBtn.Pressed += OpenInvitePrompt;

        var leaveBtn = SmallButton("Leave", btnRow);
        leaveBtn.Pressed += () => { if (InParty) Net.I.SendPartyLeave(_myId); };

        _partyDisbandBtn = SmallButton("Disband", btnRow);
        _partyDisbandBtn.Pressed += () => { if (AmLeader) Net.I.SendPartyDisband(); };

        var seekBtn = SmallButton("Seek Party", btnRow);
        seekBtn.Pressed += ToggleSeekParty;

        _partyCtxMenu = new PopupMenu();
        _partyCtxMenu.IdPressed += OnPartyCtxAction;
        _partyLayer.AddChild(_partyCtxMenu);

        BuildInvitePrompts();
        RefreshPartyUI();
    }

    private static Button SmallButton(string text, Container parent)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 12);
        parent.AddChild(b);
        return b;
    }

    private void RefreshPartyUI()
    {
        if (_partyMembersBox == null) return;
        foreach (Node child in _partyMembersBox.GetChildren())
        {
            _partyMembersBox.RemoveChild(child);
            child.QueueFree();
        }

        _partyHeaderLbl.Text = InParty ? $"Party  ({PartyMembers.Count}/{PartyMaxMembers})" : "Party";
        _partyDisbandBtn.Disabled = !AmLeader;

        if (!InParty)
        {
            var hint = HudStyle.Label(13);
            hint.Text = "Not in a party.\nInvite a player, or use Seek Party.";
            _partyMembersBox.AddChild(hint);
            return;
        }

        for (int i = 0; i < PartyMembers.Count; i++)
            _partyMembersBox.AddChild(BuildMemberRow(PartyMembers[i], isLeader: i == 0));
    }

    private Control BuildMemberRow(PartyMember m, bool isLeader)
    {
        int hp = m.Hp, maxHp = m.MaxHp, mp = m.Mp, maxMp = m.MaxMp;
        int level = m.Level, cls = m.Class;
        if (m.CharId == _myId)
        {
            hp = Vitals.Hp; maxHp = Vitals.MaxHp; mp = Vitals.Mp; maxMp = Vitals.MaxMp;
            level = Sheet.Level; cls = _selfClass;
        }

        var row = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        row.AddThemeConstantOverride("separation", 2);

        var head = HudStyle.Label(14);
        string lead = isLeader ? "★ " : "";
        string me = m.CharId == _myId ? "  (you)" : "";
        head.Text = $"{lead}{m.Name}{me}";
        if (isLeader) head.AddThemeColorOverride("font_color", new Color("ffd24a"));
        row.AddChild(head);

        var sub = HudStyle.Label(11);
        sub.Text = $"Lv {level}   {ClassName(cls)}";
        sub.AddThemeColorOverride("font_color", new Color("b9c0c8"));
        row.AddChild(sub);

        if (Net.I.PartyStatusOf(m.CharId) is { Count: > 0 } status)
        {
            var names = new List<string>();
            foreach (var t in status) { var n = StatusName(t); if (n.Length > 0) names.Add(n); }
            if (names.Count > 0)
            {
                var st = HudStyle.Label(11);
                st.Text = string.Join(", ", names);
                st.AddThemeColorOverride("font_color", new Color("d98b8b"));
                row.AddChild(st);
            }
        }

        var hpBar = new StatBar(new Color("c0392b"), new Vector2(232, 15));
        hpBar.Set(hp, maxHp);
        row.AddChild(hpBar);
        var mpBar = new StatBar(new Color("2d6fb0"), new Vector2(232, 13));
        mpBar.Set(mp, maxMp);
        row.AddChild(mpBar);

        int memberId = m.CharId;
        bool self = m.CharId == _myId;
        row.GuiInput += ev =>
        {
            if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }
                && AmLeader && !self)
            {
                _ctxMemberId = memberId;
                _partyCtxMenu.Clear();
                _partyCtxMenu.AddItem("Promote to leader", 1);
                _partyCtxMenu.AddItem("Kick from party", 2);
                _partyCtxMenu.Position = (Vector2I)GetViewport().GetMousePosition();
                _partyCtxMenu.Popup();
            }
        };
        return row;
    }

    private void OnPartyCtxAction(long id)
    {
        if (_ctxMemberId < 0 || !AmLeader) return;
        if (id == 1) Net.I.SendPartyPromote(_ctxMemberId);
        else if (id == 2) Net.I.SendPartyLeave(_ctxMemberId);
        _ctxMemberId = -1;
    }

    private void ToggleParty() => ToggleMainWindow("Party");

    private void ShowParty()
    {
        if (PartyTabOpen()) { RefreshPartyUI(); return; }
        ShowMainWindow("Party");
    }

    private void BuildInvitePrompts()
    {
        _inviteNamePanel = new PanelContainer { Visible = false, Position = new Vector2(16, 96) };
        _partyLayer.AddChild(_inviteNamePanel);
        var im = new MarginContainer();
        foreach (var s in new[] { "left", "right", "top", "bottom" }) im.AddThemeConstantOverride($"margin_{s}", 8);
        _inviteNamePanel.AddChild(im);
        var ir = new VBoxContainer { CustomMinimumSize = new Vector2(252, 0) };
        ir.AddThemeConstantOverride("separation", 5);
        im.AddChild(ir);
        var il = HudStyle.Label(13); il.Text = "Invite player by name:"; ir.AddChild(il);
        _inviteNameInput = new LineEdit { MaxLength = 20, PlaceholderText = "Character name" };
        _inviteNameInput.TextSubmitted += _ => ConfirmInvite();
        ir.AddChild(_inviteNameInput);
        var irow = new HBoxContainer(); irow.AddThemeConstantOverride("separation", 5); ir.AddChild(irow);
        SmallButton("Send", irow).Pressed += ConfirmInvite;
        SmallButton("Cancel", irow).Pressed += () => _inviteNamePanel.Visible = false;

        _partyDialogLayer = new CanvasLayer { Layer = 72 };
        AddChild(_partyDialogLayer);
        _inviteAskDialog = new ConfirmationDialog
        {
            Title = "Party Invite",
            OkButtonText = "Join",
            Exclusive = false,
        };
        _inviteAskDialog.GetCancelButton().Text = "Decline";
        _inviteAskDialog.Confirmed += () => AnswerInvite(true);
        _inviteAskDialog.Canceled += () => AnswerInvite(false);
        _partyDialogLayer.AddChild(_inviteAskDialog);
    }

    private void OpenInvitePrompt()
    {
        _inviteNamePanel.Visible = true;
        _inviteNameInput.Clear();
        _inviteNameInput.GrabFocus();
    }

    private void ConfirmInvite()
    {
        string name = _inviteNameInput.Text.Trim();
        _inviteNamePanel.Visible = false;
        if (name.Length == 0) return;
        if (InParty) Net.I.SendPartyInvite(name);
        else Net.I.SendPartyCreate(name);
        CombatNotice($"Inviting {name} to your party…");
    }

    private void OnPartyInvite(int inviterId, string inviterName)
    {
        _invitePending = true;
        _inviteAskDialog.DialogText = $"{inviterName} invites you to a party.\nJoin?";
        _inviteAskDialog.PopupCentered();
        CombatNotice($"{inviterName} invites you to a party. (P to open the party window)");
    }

    private void AnswerInvite(bool accept)
    {
        if (!_invitePending) return;
        _invitePending = false;
        Net.I.SendPartyAnswer(accept);
    }

    private void OnPartyMember(PartyMember m)
    {
        bool justJoined = !PartyTabOpen() && PartyMembers.Count >= 2;
        RefreshPartyUI();
        if (justJoined) ShowParty();
    }

    private void OnPartyError(int code)
    {
        CombatNotice(code switch
        {
            -2 => "Party invite failed: level difference is more than 8.",
            -3 => "Party invite failed: target is in a different zone.",
            _  => "Party invite failed.",
        });
    }

    private void OnPartyRemoved(int memberId, string who)
    {
        if (memberId == _myId) CombatNotice("You left the party.");
        else if (who.Length > 0) CombatNotice($"{who} left the party.");
        RefreshPartyUI();
    }

    private void OnPartyDisband()
    {
        CombatNotice("The party has been disbanded.");
        RefreshPartyUI();
    }

    private void OnPartySelfHp(int hp, int maxHp, int attackerId) => RefreshPartyRows();
    private void OnPartySelfMp(int mp, int maxMp) => RefreshPartyRows();

    private void OnPartyMemberStats(int cid, int maxHp, int hp, int maxMp, int mp) => RefreshPartyRows();

    private void OnPartyMemberLevel(int cid, int level) => RefreshPartyRows();

    private void OnPartyMemberClass(int cid, int cls) => RefreshPartyRows();

    private void OnPartyMemberStatus(int cid, byte statusType, bool applied) => RefreshPartyRows();

    private void RefreshPartyRows()
    {
        if (PartyTabOpen() && InParty) RefreshPartyUI();
    }

    private static string StatusName(byte type) => type switch
    {
        1 => "dot", 2 => "poison", 3 => "disease", 4 => "blind", 5 => "low hp", _ => "",
    };
}
