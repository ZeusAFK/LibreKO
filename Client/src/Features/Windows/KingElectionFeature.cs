using Godot;

namespace LibreKO.Features.Windows;

internal sealed class KingElectionFeature : IFeature, IEscapable
{
    private readonly IWorldContext context;
    private readonly KingSession king;
    private readonly NpcQuestSession npcQuest;
    private readonly IKingRequests requests;
    private readonly IInteractionDialogs interactionDialogs;

    public KingElectionFeature(IWorldContext context, KingSession king, NpcQuestSession npcQuest, IKingRequests requests, IInteractionDialogs interactionDialogs)
    {
        this.context = context;
        this.king = king;
        this.npcQuest = npcQuest;
        this.requests = requests;
        this.interactionDialogs = interactionDialogs;
    }

    private const string ElectionTitle = "King Election";

    private CanvasLayer layer = null!;
    private bool boxOpen;

    public void Build()
    {
        BuildWindows();
        foreach (var panel in Panels) interactionDialogs.InteractionCloses(panel);

        king.KingElectionOpenEvent += OpenElection;
        king.KingScheduleEvent += OnSchedule;
        king.KingResultEvent += OnResult;
        king.KingCandidatesEvent += OnCandidates;
        king.KingPlanEvent += OnPlan;
        king.KingShoutEvent += OnShout;
        king.KingSenatorsEvent += OnSenators;
        king.KingImpeachmentProposedEvent += OnImpeachmentProposed;
    }

    private IClosableWindow[]? panels;

    private IReadOnlyList<IClosableWindow> Panels => panels ??=
    [
        new ClosablePanel(() => voteShown, CloseVote),
        new ClosablePanel(() => planShown, ClosePlanEditor),
        new ClosablePanel(() => nominateShown, CloseNominate),
        new ClosablePanel(() => electionShown, CloseElection),
    ];

    bool IEscapable.CloseOnEscape()
    {
        if (!ballotShown) return EscapeOrder.CloseFirst(Panels);
        PressBallot(null);
        return true;
    }

    public void Dispose()
    {
        king.KingElectionOpenEvent -= OpenElection;
        king.KingScheduleEvent -= OnSchedule;
        king.KingResultEvent -= OnResult;
        king.KingCandidatesEvent -= OnCandidates;
        king.KingPlanEvent -= OnPlan;
        king.KingShoutEvent -= OnShout;
        king.KingSenatorsEvent -= OnSenators;
        king.KingImpeachmentProposedEvent -= OnImpeachmentProposed;
    }

    internal void BuildWindows()
    {
        layer = new CanvasLayer { Layer = UiLayers.ServiceWindows };
        context.Root.AddChild(layer);
        BuildElection();
        BuildNominate();
        BuildPlanEditor();
        BuildVote();
        BuildBallot();
    }

    private void Message(string text, string title = ElectionTitle) => KingMessages.Show(context.Root, text, title);

    private void ResultMessage(KingReply reply, short result) => KingMessages.Result(context.Root, reply, result, ElectionTitle);

    private void Confirm(KingBox box, string text, string subject = "", bool fromPush = false)
    {
        boxOpen = true;
        if (KingElection.TwoChoices(box))
        {
            OpenBallot(box, text, fromPush);
            return;
        }
        Notice.Confirm(context.Root, text, NpcServiceKit.Text(KingElection.OkText, "OK"), NpcServiceKit.CancelLabel,
            () => { boxOpen = false; BoxAccepted(box, subject); },
            () => { boxOpen = false; BoxCancelled(box); },
            ElectionTitle);
    }

    private void BoxAccepted(KingBox box, string subject)
    {
        switch (box)
        {
            case KingBox.Nominate:
                requests.Nominate(subject);
                break;
            case KingBox.TurnDown:
                busy = true;
                requests.Withdraw();
                break;
            case KingBox.Propose:
                requests.ImpeachmentPropose();
                break;
        }
    }

    private void BoxCancelled(KingBox box)
    {
        switch (box)
        {
            case KingBox.Nominate:
                ShowNominate();
                break;
            case KingBox.TurnDown:
                ShowElectionPage(ElectionPage.Election);
                break;
            case KingBox.Propose:
            case KingBox.SenatorBallot:
                ShowElectionPage(ElectionPage.Impeachment, ElectionPage.Proposal);
                break;
            case KingBox.PublicBallot:
                ShowElectionPage(ElectionPage.Impeachment);
                break;
        }
    }

    private void OnResult(KingReply reply, short result)
    {
        switch (reply)
        {
            case KingReply.Nominate:
            case KingReply.Withdraw:
            case KingReply.Propose:
            case KingReply.SenatorVote:
            case KingReply.PublicVote:
                busy = false;
                ResultMessage(reply, result);
                break;
            case KingReply.PlanPosted:
                planBusy = false;
                if (result == KingWire.Success) ClosePlanEditor();
                ResultMessage(reply, result);
                break;
            case KingReply.Board:
                busy = false;
                if (result == KingWire.Success) ShowElectionPage(ElectionPage.Board);
                else ResultMessage(reply, result);
                break;
            case KingReply.Vote:
                voteBusy = false;
                CloseVote();
                ResultMessage(reply, result);
                break;
            case KingReply.SenatorBallot:
            case KingReply.PublicBallot:
                busy = false;
                if (result != KingWire.Success)
                {
                    ResultMessage(reply, result);
                    break;
                }
                HideElection();
                bool senators = reply == KingReply.SenatorBallot;
                Confirm(senators ? KingBox.SenatorBallot : KingBox.PublicBallot,
                    NpcServiceKit.Text(senators ? KingElection.SenatorBallotText : KingElection.PublicBallotText));
                break;
        }
    }

    private void OnImpeachmentProposed()
    {
        HideElection();
        Confirm(KingBox.SenatorBallot, NpcServiceKit.Text(KingElection.SenatorBallotText), fromPush: true);
    }

    private const int ElectionWidth = 400;
    private const float SpeechHeight = 68f;
    private const float SenatorsHeight = 84f;
    private const string ElectionOfficerTitle = "Election Officer";
    private const int ElectionRowsMax = 5;

    internal HudWindow ElectionPanel { get; private set; } = null!;
    private Label speechUpper = null!;
    private Label speechLower = null!;
    private Label senatorsLabel = null!;
    private ScrollContainer senatorsBox = null!;
    private readonly Button[] options = new Button[ElectionRowsMax];
    private readonly ElectionPager pager = new();
    private string name = "";
    private string senatorNames = "";
    private KingSchedule schedule;
    private int scheduleKind;
    private bool electionShown;
    private bool busy;
    private bool nominatePending;

    private void BuildElection()
    {
        ElectionPanel = NpcServiceKit.ServiceWindow(layer, "kingelection", ElectionOfficerTitle, ElectionWidth, CloseElection);
        var body = ElectionPanel.Body;
        body.AddChild(NpcServiceKit.NpcSpeech(SpeechHeight, out speechUpper, out speechLower));

        senatorsBox = new ScrollContainer
        {
            Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, SenatorsHeight),
        };
        UiTheme.ThinScrollbar(senatorsBox.GetVScrollBar());
        speechUpper.GetParent().AddChild(senatorsBox);
        senatorsLabel = NpcServiceKit.SpeechLabel(UiTheme.Gold);
        senatorsBox.AddChild(senatorsLabel);

        var optionsBox = new VBoxContainer();
        optionsBox.AddThemeConstantOverride("separation", 5);
        body.AddChild(optionsBox);
        for (int i = 0; i < ElectionRowsMax; i++)
        {
            int index = i;
            options[i] = NpcServiceKit.NpcOption("", () => PressElectionRow(index));
            optionsBox.AddChild(options[i]);
        }
    }

    internal void OpenElection(string kingName)
    {
        name = kingName;
        busy = false;
        nominatePending = false;
        pager.Reset();
        ElectionPanel.Title = NpcServiceKit.WindowTitle(npcQuest, ElectionOfficerTitle);
        ShowElectionPage(ElectionPage.Main);
    }

    private void ShowElectionPage(params ElectionPage[] pages)
    {
        foreach (var page in pages) pager.Go(page);
        RenderElection();
        electionShown = true;
        ElectionPanel.Visible = true;
    }

    private void GoElectionPage(ElectionPage page)
    {
        pager.Go(page);
        RenderElection();
    }

    private void HideElection()
    {
        electionShown = false;
        ElectionPanel.Visible = false;
    }

    internal void CloseElection()
    {
        busy = false;
        nominatePending = false;
        HideElection();
    }

    private void RenderElection()
    {
        var page = pager.Current;
        string upper = page == ElectionPage.Schedule
            ? NpcServiceKit.Fill(KingElection.ScheduleText((KingScheduleKind)scheduleKind), "",
                KingElection.ScheduleArgs(schedule.Month, schedule.Day, schedule.Hour, schedule.Minute))
            : NpcServiceKit.Text(KingElection.PageText(page));
        string lower = page == ElectionPage.Main ? NpcServiceKit.Fill(KingElection.KingLine(name), "", name) : "";
        NpcServiceKit.SetSpeech(speechUpper, upper);
        NpcServiceKit.SetSpeech(speechLower, lower);

        senatorsBox.Visible = page == ElectionPage.Senators;
        senatorsLabel.Text = senatorNames;

        var rows = KingElection.Rows(page);
        for (int i = 0; i < options.Length; i++)
        {
            options[i].Visible = i < rows.Count;
            if (i < rows.Count) options[i].Text = NpcServiceKit.Text(rows[i].TextId);
        }
    }

    internal void PressElectionRow(int index)
    {
        var rows = KingElection.Rows(pager.Current);
        if (index >= rows.Count || busy || boxOpen) return;
        switch (rows[index].Action)
        {
            case ElectionAction.AskSchedule:
                busy = true;
                requests.Schedule();
                break;
            case ElectionAction.ToElection:
                GoElectionPage(ElectionPage.Election);
                break;
            case ElectionAction.ToImpeachment:
                GoElectionPage(ElectionPage.Impeachment);
                break;
            case ElectionAction.ToProposal:
                GoElectionPage(ElectionPage.Proposal);
                break;
            case ElectionAction.ToMain:
                GoElectionPage(ElectionPage.Main);
                break;
            case ElectionAction.Back:
                pager.Back();
                RenderElection();
                break;
            case ElectionAction.Nominate:
                busy = true;
                nominatePending = true;
                requests.Schedule();
                break;
            case ElectionAction.TurnDown:
                Confirm(KingBox.TurnDown, NpcServiceKit.Text(KingElection.TurnDownText));
                break;
            case ElectionAction.OpenPlanEditor:
                HideElection();
                OpenPlanEditor();
                break;
            case ElectionAction.AskCandidates:
                HideElection();
                requests.Candidates();
                break;
            case ElectionAction.AskPlans:
                HideElection();
                voteBusy = true;
                requests.PlanList();
                break;
            case ElectionAction.AskSenators:
                busy = true;
                requests.Senators();
                break;
            case ElectionAction.AskPublicBallot:
                busy = true;
                requests.PublicBallot();
                break;
            case ElectionAction.AskSenatorBallot:
                busy = true;
                requests.SenatorBallot();
                break;
            case ElectionAction.Propose:
                HideElection();
                Confirm(KingBox.Propose, NpcServiceKit.Text(KingElection.ProposeText));
                break;
        }
    }

    internal void OnSchedule(KingSchedule schedule)
    {
        busy = false;
        if (schedule.Kind == KingScheduleKind.None)
        {
            nominatePending = false;
            if (KingElection.SenatorsAreVoting(schedule.SenatorState)) context.Log.Notice(NpcServiceKit.Text(KingElection.SenatorsVotingText));
            return;
        }
        if (KingElection.KnownScheduleKind(schedule.Kind)) scheduleKind = (byte)schedule.Kind;
        this.schedule = schedule;
        if (nominatePending)
        {
            nominatePending = false;
            HideElection();
            OpenNominate(KingElection.NominateWindowHour((KingScheduleKind)scheduleKind, schedule.Hour));
            return;
        }
        GoElectionPage(ElectionPage.Schedule);
    }

    internal void OnSenators(KingSenators senators)
    {
        busy = false;
        if (senators.Result != KingWire.Success)
        {
            ResultMessage(KingReply.Senators, senators.Result);
            return;
        }
        senatorNames = KingElection.Senators(senators.Names);
        GoElectionPage(ElectionPage.Senators);
    }

    private const int NominateWidth = 400;
    private const float NominateSpeechHeight = 96f;
    private const int PlanWidth = 420;
    private const float PlanHeight = 190f;
    private const float NomineeEditWidth = 200f;
    private const int NomineeBoardText = 11342;
    private const string NominateNote = " - Please confirm the character ID. the nomination process cannot be repeated.";
    private const string IdLabel = "ID";

    internal HudWindow NominatePanel { get; private set; } = null!;
    internal HudWindow PlanPanel { get; private set; } = null!;
    private Label nominateText = null!;
    private Label planCount = null!;
    internal LineEdit NomineeEdit { get; private set; } = null!;
    internal TextEdit PlanEdit { get; private set; } = null!;
    private bool nominateShown;
    private bool planShown;
    private bool planBusy;

    private void BuildNominate()
    {
        NominatePanel = NpcServiceKit.ServiceWindow(layer, "kingnominate", ElectionOfficerTitle, NominateWidth, CloseNominate);
        var body = NominatePanel.Body;
        body.AddChild(NpcServiceKit.NpcSpeech(NominateSpeechHeight, out nominateText, out var note));
        NpcServiceKit.SetSpeech(note, NominateNote);

        var idRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        idRow.AddThemeConstantOverride("separation", 10);
        body.AddChild(idRow);
        var id = UiTheme.Text(IdLabel, 13, UiTheme.Gold);
        id.VerticalAlignment = VerticalAlignment.Center;
        idRow.AddChild(id);
        NomineeEdit = new LineEdit { CustomMinimumSize = new Vector2(NomineeEditWidth, 0) };
        NomineeEdit.TextSubmitted += _ => ConfirmNominee();
        idRow.AddChild(NomineeEdit);

        var confirm = UiTheme.ActionButton(NpcServiceKit.ConfirmLabel, "");
        confirm.Pressed += ConfirmNominee;
        var cancel = UiTheme.SmallButton(NpcServiceKit.CancelLabel, "");
        cancel.Pressed += CancelNominate;
        body.AddChild(NpcServiceKit.FooterButtons(confirm, cancel));
    }

    internal void OpenNominate(int hour)
    {
        var (textId, from, until) = KingElection.NominateHours(hour);
        nominateText.Text = NpcServiceKit.Fill(textId, "", from, until);
        NominatePanel.Title = NpcServiceKit.WindowTitle(npcQuest, ElectionOfficerTitle);
        NomineeEdit.Text = "";
        ShowNominate();
    }

    private void ShowNominate()
    {
        nominateShown = true;
        NominatePanel.Visible = true;
        if (NomineeEdit.IsInsideTree()) NomineeEdit.GrabFocus();
    }

    internal void CloseNominate()
    {
        nominateShown = false;
        NominatePanel.Visible = false;
    }

    private void CancelNominate()
    {
        CloseNominate();
        ShowElectionPage(ElectionPage.Election);
    }

    private void ConfirmNominee()
    {
        string name = NomineeEdit.Text.Trim();
        int refusal = KingElection.NomineeRefusal(name);
        if (refusal != 0)
        {
            Message(NpcServiceKit.Text(refusal));
            return;
        }
        CloseNominate();
        Confirm(KingBox.Nominate, NpcServiceKit.Fill(KingElection.ConfirmNominateText, "", name), name);
    }

    private void BuildPlanEditor()
    {
        PlanPanel = NpcServiceKit.ServiceWindow(layer, "kingplan", NpcServiceKit.Text(NomineeBoardText, "Nominee Board"), PlanWidth,
            CancelPlanEditor);
        var body = PlanPanel.Body;

        PlanEdit = new TextEdit
        {
            CustomMinimumSize = new Vector2(0, PlanHeight),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        NpcServiceKit.StyleTextBox(PlanEdit);
        PlanEdit.TextChanged += RefreshPlanCount;
        body.AddChild(PlanEdit);

        planCount = UiTheme.Text("", 12, UiTheme.TextLo, HorizontalAlignment.Right);
        body.AddChild(planCount);

        var confirm = UiTheme.ActionButton(NpcServiceKit.ConfirmLabel, "");
        confirm.Pressed += PostPlan;
        var cancel = UiTheme.SmallButton(NpcServiceKit.CancelLabel, "");
        cancel.Pressed += CancelPlanEditor;
        body.AddChild(NpcServiceKit.FooterButtons(confirm, cancel));
        RefreshPlanCount();
    }

    internal void OpenPlanEditor()
    {
        planBusy = false;
        PlanEdit.Text = "";
        RefreshPlanCount();
        planShown = true;
        PlanPanel.Visible = true;
        if (PlanEdit.IsInsideTree()) PlanEdit.GrabFocus();
    }

    internal void ClosePlanEditor()
    {
        planShown = false;
        PlanPanel.Visible = false;
    }

    private void CancelPlanEditor()
    {
        ClosePlanEditor();
        ShowElectionPage(ElectionPage.Election, ElectionPage.Board);
    }

    internal void RefreshPlanCount()
    {
        int length = PlanEdit.Text.Length;
        planCount.Text = $"{length} / {KingElection.PlanLimit}";
        planCount.AddThemeColorOverride("font_color", length > KingElection.PlanLimit ? UiTheme.Bad : UiTheme.TextLo);
    }

    private void PostPlan()
    {
        if (planBusy) return;
        string plan = PlanEdit.Text;
        int refusal = KingElection.PlanRefusal(plan);
        if (refusal != 0)
        {
            Message(NpcServiceKit.Text(refusal));
            return;
        }
        planBusy = true;
        requests.Plan(plan);
    }

    private const int VoteWidth = 420;
    private const float VoteRowHeight = 34f;
    private const float VoteButtonWidth = 64f;
    private const float VoteButtonHeight = 24f;
    private const float VoteNumberWidth = 64f;
    private const int BallotWidth = 380;
    private const double ShoutCycleSeconds = 10.0;
    private const string VoteLabel = "Vote";
    private const string PledgeLabel = "Pledge";
    private const string NumberLabel = "Number";
    private const string CandidateLabel = "Candidate Name";

    internal HudWindow VotePanel { get; private set; } = null!;
    internal HudWindow BallotPanel { get; private set; } = null!;
    private readonly VoteRow[] voteRows = new VoteRow[KingElection.CandidateSlots];
    private Label ballotText = null!;
    private KingBox ballotBox;
    private bool voteShown;
    private bool voteBusy;
    private bool ballotShown;
    private bool ballotFromPush;
    private int shoutToken;

    private sealed record VoteRow(PanelContainer Panel, Button Vote, Label Number, Label Name, Button Pledge)
    {
        public KingCandidate? Candidate { get; set; }
    }

    private void BuildVote()
    {
        VotePanel = NpcServiceKit.ServiceWindow(layer, "kingvote", NpcServiceKit.Text(KingElection.ElectionText, "Election"), VoteWidth, CloseVote);
        var body = VotePanel.Body;

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        body.AddChild(head);
        head.AddChild(new Control { CustomMinimumSize = new Vector2(VoteButtonWidth + 8, 0) });
        head.AddChild(VoteColumn(UiTheme.SectionTitle(NumberLabel), VoteNumberWidth));
        var nameHead = UiTheme.SectionTitle(CandidateLabel);
        nameHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(nameHead);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 3);
        body.AddChild(list);
        for (int i = 0; i < voteRows.Length; i++)
        {
            var row = BuildVoteRow(i);
            voteRows[i] = row;
            list.AddChild(row.Panel);
        }
    }

    private static Control VoteColumn(Control control, float width)
    {
        control.CustomMinimumSize = new Vector2(width, 0);
        return control;
    }

    private VoteRow BuildVoteRow(int index)
    {
        var panel = UiTheme.RowPanel();
        panel.CustomMinimumSize = new Vector2(0, VoteRowHeight);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        panel.AddChild(line);

        var vote = UiTheme.SmallButton(VoteLabel, "");
        vote.CustomMinimumSize = new Vector2(VoteButtonWidth, VoteButtonHeight);
        vote.Pressed += () => AskVote(index);
        line.AddChild(vote);
        var number = UiTheme.Text("", 13, UiTheme.Gold);
        number.VerticalAlignment = VerticalAlignment.Center;
        line.AddChild(VoteColumn(number, VoteNumberWidth));
        var nameLabel = UiTheme.Text("", 13, UiTheme.TextHi);
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        nameLabel.ClipText = true;
        line.AddChild(nameLabel);
        var pledge = UiTheme.SmallButton(PledgeLabel, "");
        pledge.CustomMinimumSize = new Vector2(VoteButtonWidth, VoteButtonHeight);
        pledge.Pressed += () => AskPledge(index);
        line.AddChild(pledge);
        return new VoteRow(panel, vote, number, nameLabel, pledge);
    }

    internal void OnCandidates(KingCandidates reply)
    {
        voteBusy = false;
        if (reply.Result != KingWire.Success)
        {
            CloseVote();
            ResultMessage(KingReply.Candidates, reply.Result);
            return;
        }
        for (int i = 0; i < voteRows.Length; i++)
        {
            var row = voteRows[i];
            KingCandidate? candidate = i < reply.Candidates.Count ? reply.Candidates[i] : null;
            row.Candidate = candidate;
            bool filled = candidate != null;
            row.Vote.Visible = filled;
            row.Pledge.Visible = filled;
            row.Number.Text = filled ? candidate!.Value.Number.ToString() : "";
            row.Name.Text = filled ? (candidate!.Value.Name.Length > 0 ? candidate.Value.Name : " ") : "";
            row.Panel.AddThemeStyleboxOverride("panel", UiTheme.Row(muted: !filled));
        }
        voteShown = true;
        VotePanel.Visible = true;
    }

    internal void CloseVote()
    {
        voteShown = false;
        VotePanel.Visible = false;
    }

    private void AskVote(int index)
    {
        if (voteBusy || voteRows[index].Candidate is not { } candidate) return;
        string name = candidate.Name;
        Notice.Confirm(context.Root, NpcServiceKit.Fill(KingElection.ConfirmVoteText, "", candidate.Number.ToString(), name), NpcServiceKit.YesLabel, NpcServiceKit.NoLabel,
            () =>
            {
                voteBusy = true;
                requests.Vote(name);
            }, title: NpcServiceKit.Text(KingElection.ElectionText, "Election"));
    }

    private void AskPledge(int index)
    {
        if (voteBusy || voteRows[index].Candidate is not { } candidate) return;
        voteBusy = true;
        requests.PlanRead(candidate.Name);
    }

    private void OnPlan(KingPlan plan)
    {
        voteBusy = false;
        if (plan.Result == KingWire.Success) Message(plan.Text, PledgeLabel);
        else ResultMessage(KingReply.PlanRead, plan.Result);
    }

    private void OnShout(KingShout shout)
    {
        int token = ++shoutToken;
        if (shout.Result == KingWire.ShoutAskAgain)
        {
            requests.PlanShoutAgain();
            return;
        }
        if (shout.Result != KingWire.Success || shout.Lines.Count == 0) return;
        double each = ShoutCycleSeconds / shout.Lines.Count;
        for (int i = 0; i < shout.Lines.Count; i++)
        {
            var line = shout.Lines[i];
            string text = $"[No. {line.Number} {line.Name}]\n{line.Plan}";
            if (i == 0)
            {
                context.NpcDialog.Balloon([text]);
                continue;
            }
            context.Root.After(each * i, () =>
            {
                if (token == shoutToken) context.NpcDialog.Balloon([text]);
            });
        }
    }

    private void BuildBallot()
    {
        BallotPanel = NpcServiceKit.ServiceWindow(layer, "kingballot", NpcServiceKit.Text(KingElection.ImpeachmentText, "Impeachment"), BallotWidth,
            () => PressBallot(null));
        var body = BallotPanel.Body;
        var box = UiTheme.Section();
        body.AddChild(box);
        ballotText = NpcServiceKit.SpeechLabel(UiTheme.TextHi);
        ballotText.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(ballotText);

        var inFavour = UiTheme.ActionButton(NpcServiceKit.Text(KingElection.InFavourText, "In favor"), "");
        inFavour.Pressed += () => PressBallot(true);
        var against = UiTheme.SmallButton(NpcServiceKit.Text(KingElection.AgainstText, "Against"), "");
        against.Pressed += () => PressBallot(false);
        var cancel = UiTheme.SmallButton(NpcServiceKit.CancelLabel, "");
        cancel.Pressed += () => PressBallot(null);
        body.AddChild(NpcServiceKit.FooterButtons(inFavour, against, cancel));
    }

    internal void OpenBallot(KingBox box, string text, bool fromPush)
    {
        ballotBox = box;
        ballotFromPush = fromPush;
        ballotText.Text = text;
        ballotShown = true;
        BallotPanel.Visible = true;
    }

    private void CloseBallot()
    {
        boxOpen = false;
        ballotShown = false;
        BallotPanel.Visible = false;
    }

    internal void PressBallot(bool? inFavour)
    {
        if (!ballotShown) return;
        CloseBallot();
        bool senators = ballotBox == KingBox.SenatorBallot;
        if (inFavour is { } vote)
        {
            busy = true;
            if (senators) requests.SenatorVote(vote);
            else requests.PublicVote(vote);
            return;
        }
        if (!(senators && ballotFromPush)) BoxCancelled(ballotBox);
    }
}
