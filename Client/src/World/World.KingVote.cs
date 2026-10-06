using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const int KingVoteWidth = 420;
    private const float KingVoteRowHeight = 34f;
    private const float KingVoteButtonWidth = 64f;
    private const float KingVoteButtonHeight = 24f;
    private const float KingVoteNumberWidth = 64f;
    private const int KingElectionText = 11343;
    private const int KingImpeachmentText = 11338;
    private const int KingBallotWidth = 380;
    private const double KingShoutCycleSeconds = 10.0;
    private const string VoteLabel = "Vote";
    private const string PledgeLabel = "Pledge";
    private const string NumberLabel = "Number";
    private const string CandidateLabel = "Candidate Name";
    private const string YesLabel = "Yes";
    private const string NoLabel = "No";

    private HudWindow _kingVotePanel = null!, _kingBallotPanel = null!;
    private readonly KingVoteRow[] _kingVoteRows = new KingVoteRow[KingElection.CandidateSlots];
    private Label _kingBallotText = null!;
    private KingBox _kingBallotBox;
    private bool _kingVoteShown, _kingVoteBusy, _kingBallotShown, _kingBallotFromPush;
    private int _kingShoutToken;

    private sealed record KingVoteRow(PanelContainer Panel, Button Vote, Label Number, Label Name, Button Pledge)
    {
        public KingCandidate? Candidate { get; set; }
    }

    private void BuildKingVote()
    {
        _kingVotePanel = ServiceWindow(_kingLayer, "kingvote", KingText(KingElectionText, "Election"), KingVoteWidth, CloseKingVote);
        var body = _kingVotePanel.Body;

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 8);
        body.AddChild(head);
        head.AddChild(new Control { CustomMinimumSize = new Vector2(KingVoteButtonWidth + 8, 0) });
        head.AddChild(VoteColumn(UiTheme.SectionTitle(NumberLabel), KingVoteNumberWidth));
        var nameHead = UiTheme.SectionTitle(CandidateLabel);
        nameHead.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(nameHead);

        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 3);
        body.AddChild(list);
        for (int i = 0; i < _kingVoteRows.Length; i++)
        {
            var row = BuildKingVoteRow(i);
            _kingVoteRows[i] = row;
            list.AddChild(row.Panel);
        }
    }

    private static Control VoteColumn(Control control, float width)
    {
        control.CustomMinimumSize = new Vector2(width, 0);
        return control;
    }

    private KingVoteRow BuildKingVoteRow(int index)
    {
        var panel = UiTheme.RowPanel();
        panel.CustomMinimumSize = new Vector2(0, KingVoteRowHeight);
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 8);
        panel.AddChild(line);

        var vote = UiTheme.SmallButton(VoteLabel, "");
        vote.CustomMinimumSize = new Vector2(KingVoteButtonWidth, KingVoteButtonHeight);
        vote.Pressed += () => AskKingVote(index);
        line.AddChild(vote);
        var number = UiTheme.Text("", 13, UiTheme.Gold);
        number.VerticalAlignment = VerticalAlignment.Center;
        line.AddChild(VoteColumn(number, KingVoteNumberWidth));
        var name = UiTheme.Text("", 13, UiTheme.TextHi);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        name.ClipText = true;
        line.AddChild(name);
        var pledge = UiTheme.SmallButton(PledgeLabel, "");
        pledge.CustomMinimumSize = new Vector2(KingVoteButtonWidth, KingVoteButtonHeight);
        pledge.Pressed += () => AskKingPledge(index);
        line.AddChild(pledge);
        return new KingVoteRow(panel, vote, number, name, pledge);
    }

    private void OnKingCandidates(KingCandidates reply)
    {
        _kingVoteBusy = false;
        if (reply.Result != KingElection.Success)
        {
            CloseKingVote();
            KingResultMessage(KingReply.Candidates, reply.Result);
            return;
        }
        for (int i = 0; i < _kingVoteRows.Length; i++)
        {
            var row = _kingVoteRows[i];
            KingCandidate? candidate = i < reply.Candidates.Count ? reply.Candidates[i] : null;
            row.Candidate = candidate;
            bool filled = candidate != null;
            row.Vote.Visible = filled;
            row.Pledge.Visible = filled;
            row.Number.Text = filled ? candidate!.Value.Number.ToString() : "";
            row.Name.Text = filled ? (candidate!.Value.Name.Length > 0 ? candidate.Value.Name : " ") : "";
            row.Panel.AddThemeStyleboxOverride("panel", UiTheme.Row(muted: !filled));
        }
        _kingVoteShown = true;
        _kingVotePanel.Visible = true;
    }

    private void CloseKingVote()
    {
        _kingVoteShown = false;
        _kingVotePanel.Visible = false;
    }

    private void AskKingVote(int index)
    {
        if (_kingVoteBusy || _kingVoteRows[index].Candidate is not { } candidate) return;
        string name = candidate.Name;
        Notice.Confirm(this, KingFill(KingElection.ConfirmVoteText, "", candidate.Number.ToString(), name), YesLabel, NoLabel,
            () =>
            {
                _kingVoteBusy = true;
                Net.I.SendKingVote(name);
            }, title: KingText(KingElectionText, "Election"));
    }

    private void AskKingPledge(int index)
    {
        if (_kingVoteBusy || _kingVoteRows[index].Candidate is not { } candidate) return;
        _kingVoteBusy = true;
        Net.I.SendKingPlanRead(candidate.Name);
    }

    private void OnKingPlan(KingPlan plan)
    {
        _kingVoteBusy = false;
        if (plan.Result == KingElection.Success) KingMessage(plan.Text, PledgeLabel);
        else KingResultMessage(KingReply.PlanRead, plan.Result);
    }

    private void OnKingShout(KingShout shout)
    {
        int token = ++_kingShoutToken;
        if (shout.Result == KingElection.ShoutAskAgain)
        {
            Net.I.SendKingPlanShoutAgain();
            return;
        }
        if (shout.Result != KingElection.Success || shout.Lines.Count == 0) return;
        double each = KingShoutCycleSeconds / shout.Lines.Count;
        for (int i = 0; i < shout.Lines.Count; i++)
        {
            var line = shout.Lines[i];
            string text = $"[No. {line.Number} {line.Name}]\n{line.Plan}";
            if (i == 0)
            {
                ShowNpcBalloon([text]);
                continue;
            }
            GetTree().CreateTimer(each * i).Timeout += () =>
            {
                if (token == _kingShoutToken) ShowNpcBalloon([text]);
            };
        }
    }

    private void BuildKingBallot()
    {
        _kingBallotPanel = ServiceWindow(_kingLayer, "kingballot", KingText(KingImpeachmentText, "Impeachment"), KingBallotWidth,
            () => PressKingBallot(null));
        var body = _kingBallotPanel.Body;
        var box = UiTheme.Section();
        body.AddChild(box);
        _kingBallotText = SpeechLabel(UiTheme.TextHi);
        _kingBallotText.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(_kingBallotText);

        var inFavour = UiTheme.ActionButton(KingText(KingElection.InFavourText, "In favor"), "");
        inFavour.Pressed += () => PressKingBallot(true);
        var against = UiTheme.SmallButton(KingText(KingElection.AgainstText, "Against"), "");
        against.Pressed += () => PressKingBallot(false);
        var cancel = UiTheme.SmallButton(CancelLabel, "");
        cancel.Pressed += () => PressKingBallot(null);
        body.AddChild(FooterButtons(inFavour, against, cancel));
    }

    private void OpenKingBallot(KingBox box, string text, bool fromPush)
    {
        _kingBallotBox = box;
        _kingBallotFromPush = fromPush;
        _kingBallotText.Text = text;
        _kingBallotShown = true;
        _kingBallotPanel.Visible = true;
    }

    private void CloseKingBallot()
    {
        _kingBoxOpen = false;
        _kingBallotShown = false;
        _kingBallotPanel.Visible = false;
    }

    private void PressKingBallot(bool? inFavour)
    {
        if (!_kingBallotShown) return;
        CloseKingBallot();
        bool senators = _kingBallotBox == KingBox.SenatorBallot;
        if (inFavour is { } vote)
        {
            _kingBusy = true;
            if (senators) Net.I.SendKingSenatorVote(vote);
            else Net.I.SendKingPublicVote(vote);
            return;
        }
        if (!(senators && _kingBallotFromPush)) KingBoxCancelled(_kingBallotBox);
    }
}
