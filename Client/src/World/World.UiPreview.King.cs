using Godot;
using LibreKO.Domain;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const string KingPreviewName = "Zeus";
    private const string KingPreviewPlan =
        "A stronger nation through fair taxes. Every clan gets a voice in the senate, the treasury funds the Border Defense War, "
        + "and the war chest is opened to anyone defending Eslant.";
    private const int KingPreviewTribute = 12_500_000;
    private const int KingPreviewTreasury = 845_300_000;
    private const int KingPreviewTariff = 3;

    internal Control BuildKingUiPreview(string view)
    {
        ItemData.EnsureLoaded();
        var me = Net.I.LastEnter;
        me.Nation = Nations.ElMorad;
        Net.I.SeedPreviewEnter(me);
        BuildKingWindows();

        switch (view)
        {
            case "king-election-schedule":
                OpenKingElection(KingPreviewName);
                OnKingSchedule(new KingSchedule(KingElection.ScheduleElection, 10, 12, 20, 0, 0));
                return DetachPreviewControl(_kingElectionPanel);
            case "king-election-candidates":
                OpenKingElection("");
                PressKingElectionRow(1);
                return DetachPreviewControl(_kingElectionPanel);
            case "king-election-impeachment":
                OpenKingElection(KingPreviewName);
                PressKingElectionRow(2);
                return DetachPreviewControl(_kingElectionPanel);
            case "king-election-senators":
                OpenKingElection(KingPreviewName);
                PressKingElectionRow(2);
                OnKingSenators(new KingSenators(KingElection.Success,
                    ["Rikka", "Hermes", "Valkyrie", "Morgana", "Sylph", "Ardream", "Krowaz", "Talon"]));
                return DetachPreviewControl(_kingElectionPanel);
            case "king-nominate":
                OpenKingNominate(20);
                _kingNomineeEdit.Text = "Rikka";
                return DetachPreviewControl(_kingNominatePanel);
            case "king-plan":
                OpenKingPlanEditor();
                _kingPlanEdit.Text = KingPreviewPlan;
                RefreshKingPlanCount();
                return DetachPreviewControl(_kingPlanPanel);
            case "king-vote":
                OnKingCandidates(new KingCandidates(KingElection.Success,
                [
                    new KingCandidate(1, "Rikka", "Valhalla"),
                    new KingCandidate(2, "Zeus", "Olympus"),
                    new KingCandidate(3, "Morgana", "Avalon"),
                    new KingCandidate(4, "Hermes", ""),
                ]));
                return DetachPreviewControl(_kingVotePanel);
            case "king-ballot":
                OpenKingBallot(KingBox.SenatorBallot, KingText(KingElection.SenatorBallotText), false);
                return DetachPreviewControl(_kingBallotPanel);
            case "nation-tax":
                OnKingTreasury(new KingTreasury(NationTreasury.KingView, KingPreviewTribute, KingPreviewTreasury));
                return DetachPreviewControl(_nationTaxPanel);
            case "nation-tax-citizen":
                OnKingTreasury(new KingTreasury(NationTreasury.CitizenView, 0, KingPreviewTreasury));
                return DetachPreviewControl(_nationTaxPanel);
            case "nation-tax-rate":
                OnKingTariffRead(new KingTariff(KingElection.Success, KingPreviewTariff));
                return DetachPreviewControl(_nationTaxRatePanel);
            case "nation-intro":
                OpenNationIntro("");
                return DetachPreviewControl(_nationIntroPanel);
            default:
                OpenKingElection(KingPreviewName);
                return DetachPreviewControl(_kingElectionPanel);
        }
    }
}
