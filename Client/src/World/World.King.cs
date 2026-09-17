using System.Collections.Generic;
using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private CanvasLayer _kingLayer = null!;
    private HudWindow _kingPanel = null!;
    private Label _kingScheduleLbl = null!, _kingStatus = null!, _kingInfoLbl = null!;
    private LineEdit _kingNomineeEdit = null!, _kingTariffEdit = null!;
    private VBoxContainer _kingCandidateList = null!;
    private Button _kingNominateBtn = null!, _kingResignBtn = null!, _kingCollectBtn = null!, _kingSetTariffBtn = null!;
    private ConfirmationDialog _kingNoticeView = null!;
    private bool _kingShown;
    private bool _kingInPoll;

    private void KingInit()
    {
        BuildKingPanel();
        Net.I.KingScheduleEvent += OnKingSchedule;
        Net.I.KingNominateEvent += OnKingNominate;
        Net.I.KingPollListEvent += OnKingPollList;
        Net.I.KingVoteEvent += OnKingVote;
        Net.I.KingResignEvent += OnKingResign;
        Net.I.KingBoardEvent += OnKingBoard;
        Net.I.KingNpcEvent += OnKingNpc;
        Net.I.KingNationIntroEvent += OnKingNationIntro;
        Net.I.KingGovernanceEvent += OnKingGovernance;
    }

    private void KingDispose()
    {
        Net.I.KingScheduleEvent -= OnKingSchedule;
        Net.I.KingNominateEvent -= OnKingNominate;
        Net.I.KingPollListEvent -= OnKingPollList;
        Net.I.KingVoteEvent -= OnKingVote;
        Net.I.KingResignEvent -= OnKingResign;
        Net.I.KingBoardEvent -= OnKingBoard;
        Net.I.KingNpcEvent -= OnKingNpc;
        Net.I.KingNationIntroEvent -= OnKingNationIntro;
        Net.I.KingGovernanceEvent -= OnKingGovernance;
    }

    private void BuildKingPanel()
    {
        _kingLayer = new CanvasLayer { Layer = 75 };
        AddChild(_kingLayer);

        _kingPanel = new HudWindow("king", "Nation King", new Vector2(170, 90)) { Visible = false };
        _kingPanel.Closed += CloseKing;
        _kingLayer.AddChild(_kingPanel);
        var r = _kingPanel.Body;
        r.AddThemeConstantOverride("separation", 8);

        r.AddChild(UiTheme.SectionTitle("Election"));
        _kingScheduleLbl = HudStyle.Label(13);
        _kingScheduleLbl.Text = "Checking election schedule...";
        r.AddChild(_kingScheduleLbl);

        var nomRow = new HBoxContainer(); nomRow.AddThemeConstantOverride("separation", 6);
        _kingNomineeEdit = new LineEdit { PlaceholderText = "clan-chief name to nominate", CustomMinimumSize = new Vector2(200, 0) };
        nomRow.AddChild(_kingNomineeEdit);
        _kingNominateBtn = new Button { Text = "Nominate", FocusMode = Control.FocusModeEnum.None };
        _kingNominateBtn.Pressed += OnNominatePressed;
        nomRow.AddChild(_kingNominateBtn);
        _kingResignBtn = new Button { Text = "Resign", FocusMode = Control.FocusModeEnum.None };
        _kingResignBtn.Pressed += () => Net.I.SendKingResign();
        nomRow.AddChild(_kingResignBtn);
        r.AddChild(nomRow);

        var candRow = new HBoxContainer(); candRow.AddThemeConstantOverride("separation", 6);
        var candTitle = UiTheme.SectionTitle("Candidates");
        candTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        candRow.AddChild(candTitle);
        var refreshBtn = new Button { Text = "Refresh", FocusMode = Control.FocusModeEnum.None };
        refreshBtn.Pressed += RefreshKing;
        candRow.AddChild(refreshBtn);
        r.AddChild(candRow);

        _kingCandidateList = KingScroll(r, 170);

        r.AddChild(new HSeparator());

        r.AddChild(UiTheme.SectionTitle("Nation & King"));
        _kingInfoLbl = HudStyle.Label(13);
        _kingInfoLbl.Text = "King: (unknown)";
        r.AddChild(_kingInfoLbl);

        var taxRow = new HBoxContainer(); taxRow.AddThemeConstantOverride("separation", 6);
        _kingCollectBtn = new Button { Text = "Collect Tax", FocusMode = Control.FocusModeEnum.None };
        _kingCollectBtn.Pressed += () => Net.I.SendKingCollectTax();
        taxRow.AddChild(_kingCollectBtn);
        var tariffLbl = HudStyle.Label(13); tariffLbl.Text = "Tariff %:"; taxRow.AddChild(tariffLbl);
        _kingTariffEdit = new LineEdit { PlaceholderText = "0-10", CustomMinimumSize = new Vector2(60, 0) };
        taxRow.AddChild(_kingTariffEdit);
        _kingSetTariffBtn = new Button { Text = "Set", FocusMode = Control.FocusModeEnum.None };
        _kingSetTariffBtn.Pressed += OnSetTariffPressed;
        taxRow.AddChild(_kingSetTariffBtn);
        r.AddChild(taxRow);

        _kingStatus = HudStyle.Label(13);
        r.AddChild(_kingStatus);

        _kingNoticeView = new ConfirmationDialog { Title = "Campaign notice" };
        _kingNoticeView.GetCancelButton().Visible = false;
        _kingLayer.AddChild(_kingNoticeView);
    }

    private static VBoxContainer KingScroll(VBoxContainer parent, int height)
    {
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(list);
        return list;
    }

    private void ToggleKing()
    {
        if (_kingShown) { CloseKing(); return; }
        _kingPanel.Visible = true;
        _kingShown = true;
        SetKingStatus("", false);
        RefreshKing();
    }

    private void CloseKing()
    {
        if (!_kingShown) return;
        _kingShown = false;
        _kingPanel.Visible = false;
    }

    private void RefreshKing()
    {
        Net.I.SendKingSchedule();
        Net.I.SendKingPollList();
        Net.I.SendKingNationIntro();
        Net.I.SendKingNpc();
        Net.I.SendKingTariffRead();
    }

    private void OnNominatePressed()
    {
        string n = _kingNomineeEdit.Text.Trim();
        if (n.Length < 2) { SetKingStatus("Enter the clan-chief's name to nominate.", true); return; }
        Net.I.SendKingNominate(n);
    }

    private void OnSetTariffPressed()
    {
        if (int.TryParse(_kingTariffEdit.Text.Trim(), out int t) && t >= 0 && t <= 10)
            Net.I.SendKingSetTariff(t);
        else
            SetKingStatus("Tariff must be 0-10.", true);
    }

    private void OnKingSchedule(bool active, int month, int day, int hour, int minute)
    {
        _kingScheduleLbl.Text = active
            ? $"Next election: {month:00}/{day:00}  {hour:00}:{minute:00}"
            : "No election is scheduled (no royal term active).";
    }

    private void OnKingPollList(List<Net.KingCandidate> candidates)
    {
        _kingInPoll = candidates.Count > 0;
        foreach (var c in _kingCandidateList.GetChildren()) c.QueueFree();
        if (candidates.Count == 0)
        {
            var e = HudStyle.Label(13); e.Text = "No candidates have been nominated yet.";
            _kingCandidateList.AddChild(e);
            return;
        }
        foreach (var cand in candidates)
        {
            string name = cand.Name;
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Row());
            var hb = new HBoxContainer(); hb.AddThemeConstantOverride("separation", 8);
            row.AddChild(hb);
            var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            info.AddThemeConstantOverride("separation", -2);
            info.AddChild(UiTheme.Text(cand.Name, 13, UiTheme.TextHi));
            info.AddChild(UiTheme.Text(cand.Clan.Length > 0 ? $"Clan: {cand.Clan}" : "No clan", 11, UiTheme.TextLo));
            hb.AddChild(info);
            var noticeBtn = new Button { Text = "Notice", FocusMode = Control.FocusModeEnum.None };
            noticeBtn.Pressed += () => Net.I.SendKingBoardRead(name);
            hb.AddChild(noticeBtn);
            var voteBtn = new Button { Text = "Vote", FocusMode = Control.FocusModeEnum.None };
            voteBtn.Pressed += () => Net.I.SendKingVote(name);
            hb.AddChild(voteBtn);
            _kingCandidateList.AddChild(row);
        }
    }

    private void OnKingNominate(int result)
    {
        SetKingStatus(result switch
        {
            1  => "Nomination accepted.",
            -2 => "Nominations aren't open right now.",
            -3 => "You and the nominee must both be clan chiefs.",
            -4 => "That candidate is already nominated.",
            _  => "Nomination failed.",
        }, result != 1);
        if (result == 1) { _kingNomineeEdit.Text = ""; Net.I.SendKingPollList(); }
    }

    private void OnKingVote(int result)
    {
        SetKingStatus(result switch
        {
            1  => "Your vote has been counted!",
            -1 => "Voting isn't open (not in the election phase).",
            -2 => "That candidate is no longer running.",
            -3 => "You've already voted.",
            -4 => "You must be level 20 to vote.",
            _  => "Your vote couldn't be cast.",
        }, result != 1);
    }

    private void OnKingResign(int result)
    {
        SetKingStatus(result switch
        {
            1  => "You withdrew your candidacy.",
            -1 => "You can only resign during the nomination phase.",
            -2 => "You aren't a candidate.",
            _  => "Couldn't resign.",
        }, result != 1);
        if (result == 1) Net.I.SendKingPollList();
    }

    private void OnKingBoard(List<string> names, string notice)
    {
        if (notice == "__write_ok__") { SetKingStatus("Campaign notice posted.", false); return; }
        if (notice == "__write_fail__") { SetKingStatus("Couldn't post your campaign notice.", true); return; }

        if (notice.Length > 0)
        {
            _kingNoticeView.DialogText = notice;
            _kingNoticeView.PopupCentered();
        }
        else if (names.Count == 0)
        {
            SetKingStatus("This candidate hasn't posted a campaign notice.", false);
        }
    }

    private void OnKingNpc(string kingName)
    {
        if (kingName.Length > 0)
            Chat.Info($"[Kingdom] The reigning king is {kingName}.");
    }

    private void OnKingNationIntro(string kingName, int treasury, int tariff)
    {
        _kingInfoLbl.Text = kingName.Length > 0
            ? $"King: {kingName}\nNational treasury: {treasury:n0} gold    Territory tariff: {tariff}%"
            : $"King: (no king — interregnum)\nNational treasury: {treasury:n0} gold    Territory tariff: {tariff}%";
        _kingTariffEdit.Text = tariff.ToString();
    }

    private void OnKingGovernance(int main, int op, bool ok, int value)
    {
        if (main == 3)
        {
            switch (op)
            {
                case 2:
                    SetKingStatus(ok ? $"Collected {value:n0} gold in territory tax." : "No tax to collect (king only).", !ok);
                    break;
                case 3:
                    if (ok) { _kingTariffEdit.Text = value.ToString(); }
                    break;
                case 4:
                    SetKingStatus(ok ? $"Territory tariff set to {value}%." : "Couldn't set the tariff (king only, 0-10%).", !ok);
                    break;
            }
            return;
        }
        SetKingStatus(ok ? "Royal command issued." : "That royal command failed (king only / not enough treasury).", !ok);
    }

    private void SetKingStatus(string text, bool warn)
    {
        _kingStatus.Text = text;
        _kingStatus.AddThemeColorOverride("font_color", warn ? new Color("ff6a6a") : Colors.White);
    }
}
