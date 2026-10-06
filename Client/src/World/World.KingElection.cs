using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int KingElectionWidth = 400;
    private const float KingSpeechHeight = 68f;
    private const float KingSenatorsHeight = 84f;
    private const string ElectionOfficerTitle = "Election Officer";
    private const int KingElectionRowsMax = 5;

    private HudWindow _kingElectionPanel = null!;
    private Label _kingSpeechUpper = null!, _kingSpeechLower = null!, _kingSenatorsLabel = null!;
    private ScrollContainer _kingSenatorsBox = null!;
    private readonly Button[] _kingOptions = new Button[KingElectionRowsMax];
    private readonly ElectionPager _kingPager = new();
    private string _kingName = "", _kingSenatorNames = "";
    private KingSchedule _kingSchedule;
    private int _kingScheduleKind;
    private bool _kingElectionShown, _kingBusy, _kingNominatePending;

    private void BuildKingElection()
    {
        _kingElectionPanel = ServiceWindow(_kingLayer, "kingelection", ElectionOfficerTitle, KingElectionWidth, CloseKingElection);
        var body = _kingElectionPanel.Body;
        body.AddChild(NpcSpeech(KingSpeechHeight, out _kingSpeechUpper, out _kingSpeechLower));

        _kingSenatorsBox = new ScrollContainer
        {
            Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, KingSenatorsHeight),
        };
        UiTheme.ThinScrollbar(_kingSenatorsBox.GetVScrollBar());
        _kingSpeechUpper.GetParent().AddChild(_kingSenatorsBox);
        _kingSenatorsLabel = SpeechLabel(UiTheme.Gold);
        _kingSenatorsBox.AddChild(_kingSenatorsLabel);

        var options = new VBoxContainer();
        options.AddThemeConstantOverride("separation", 5);
        body.AddChild(options);
        for (int i = 0; i < KingElectionRowsMax; i++)
        {
            int index = i;
            _kingOptions[i] = NpcOption("", () => PressKingElectionRow(index));
            options.AddChild(_kingOptions[i]);
        }
    }

    private void OpenKingElection(string king)
    {
        _kingName = king;
        _kingBusy = false;
        _kingNominatePending = false;
        _kingPager.Reset();
        _kingElectionPanel.Title = NpcWindowTitle(ElectionOfficerTitle);
        ShowKingElectionPage(ElectionPage.Main);
    }

    private void ShowKingElectionPage(params ElectionPage[] pages)
    {
        foreach (var page in pages) _kingPager.Go(page);
        RenderKingElection();
        _kingElectionShown = true;
        _kingElectionPanel.Visible = true;
    }

    private void GoKingElectionPage(ElectionPage page)
    {
        _kingPager.Go(page);
        RenderKingElection();
    }

    private void HideKingElection()
    {
        _kingElectionShown = false;
        _kingElectionPanel.Visible = false;
    }

    private void CloseKingElection()
    {
        _kingBusy = false;
        _kingNominatePending = false;
        HideKingElection();
    }

    private void RenderKingElection()
    {
        var page = _kingPager.Current;
        string upper = page == ElectionPage.Schedule
            ? KingFill(KingElection.ScheduleText(_kingScheduleKind), "",
                KingElection.ScheduleArgs(_kingSchedule.Month, _kingSchedule.Day, _kingSchedule.Hour, _kingSchedule.Minute))
            : KingText(KingElection.PageText(page));
        string lower = page == ElectionPage.Main ? KingFill(KingElection.KingLine(_kingName), "", _kingName) : "";
        SetSpeech(_kingSpeechUpper, upper);
        SetSpeech(_kingSpeechLower, lower);

        _kingSenatorsBox.Visible = page == ElectionPage.Senators;
        _kingSenatorsLabel.Text = _kingSenatorNames;

        var rows = KingElection.Rows(page);
        for (int i = 0; i < _kingOptions.Length; i++)
        {
            _kingOptions[i].Visible = i < rows.Count;
            if (i < rows.Count) _kingOptions[i].Text = KingText(rows[i].TextId);
        }
    }

    private void PressKingElectionRow(int index)
    {
        var rows = KingElection.Rows(_kingPager.Current);
        if (index >= rows.Count || _kingBusy || _kingBoxOpen) return;
        switch (rows[index].Action)
        {
            case ElectionAction.AskSchedule:
                _kingBusy = true;
                Net.I.SendKingSchedule();
                break;
            case ElectionAction.ToElection:
                GoKingElectionPage(ElectionPage.Election);
                break;
            case ElectionAction.ToImpeachment:
                GoKingElectionPage(ElectionPage.Impeachment);
                break;
            case ElectionAction.ToProposal:
                GoKingElectionPage(ElectionPage.Proposal);
                break;
            case ElectionAction.ToMain:
                GoKingElectionPage(ElectionPage.Main);
                break;
            case ElectionAction.Back:
                _kingPager.Back();
                RenderKingElection();
                break;
            case ElectionAction.Nominate:
                _kingBusy = true;
                _kingNominatePending = true;
                Net.I.SendKingSchedule();
                break;
            case ElectionAction.TurnDown:
                KingConfirm(KingBox.TurnDown, KingText(KingElection.TurnDownText));
                break;
            case ElectionAction.OpenPlanEditor:
                HideKingElection();
                OpenKingPlanEditor();
                break;
            case ElectionAction.AskCandidates:
                HideKingElection();
                Net.I.SendKingCandidates();
                break;
            case ElectionAction.AskPlans:
                HideKingElection();
                _kingVoteBusy = true;
                Net.I.SendKingPlanList();
                break;
            case ElectionAction.AskSenators:
                _kingBusy = true;
                Net.I.SendKingSenators();
                break;
            case ElectionAction.AskPublicBallot:
                _kingBusy = true;
                Net.I.SendKingPublicBallot();
                break;
            case ElectionAction.AskSenatorBallot:
                _kingBusy = true;
                Net.I.SendKingSenatorBallot();
                break;
            case ElectionAction.Propose:
                HideKingElection();
                KingConfirm(KingBox.Propose, KingText(KingElection.ProposeText));
                break;
        }
    }

    private void OnKingSchedule(KingSchedule schedule)
    {
        _kingBusy = false;
        if (schedule.Kind == KingElection.ScheduleNone)
        {
            _kingNominatePending = false;
            if (KingElection.SenatorsAreVoting(schedule.SenatorState)) CombatNotice(KingText(KingElection.SenatorsVotingText));
            return;
        }
        if (KingElection.KnownScheduleKind(schedule.Kind)) _kingScheduleKind = schedule.Kind;
        _kingSchedule = schedule;
        if (_kingNominatePending)
        {
            _kingNominatePending = false;
            HideKingElection();
            OpenKingNominate(KingElection.NominateWindowHour(_kingScheduleKind, schedule.Hour));
            return;
        }
        GoKingElectionPage(ElectionPage.Schedule);
    }

    private void OnKingSenators(KingSenators senators)
    {
        _kingBusy = false;
        if (senators.Result != KingElection.Success)
        {
            KingResultMessage(KingReply.Senators, senators.Result);
            return;
        }
        _kingSenatorNames = KingElection.Senators(senators.Names);
        GoKingElectionPage(ElectionPage.Senators);
    }
}
