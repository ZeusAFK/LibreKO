using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class KingElectionTests
{
    [Fact]
    public void EachPageShowsTheClientsOwnRows()
    {
        Assert.Equal([11336, 11337, 11338], KingElection.Rows(ElectionPage.Main).Select(r => r.TextId));
        Assert.Equal([11340, 11341, 11342, 11343, 11344], KingElection.Rows(ElectionPage.Election).Select(r => r.TextId));
        Assert.Equal([11345, 11346, 11347], KingElection.Rows(ElectionPage.Board).Select(r => r.TextId));
        Assert.Equal([11348, 11349, 11350, 11351], KingElection.Rows(ElectionPage.Impeachment).Select(r => r.TextId));
        Assert.Equal([11352, 11353, 11354], KingElection.Rows(ElectionPage.Proposal).Select(r => r.TextId));
        Assert.Equal([11339], KingElection.Rows(ElectionPage.Schedule).Select(r => r.TextId));
        Assert.Equal([11355], KingElection.Rows(ElectionPage.Senators).Select(r => r.TextId));
    }

    [Fact]
    public void TheElectionPageButtonsDoWhatTheClientDoes()
    {
        Assert.Equal(
            [ElectionAction.Nominate, ElectionAction.TurnDown, ElectionAction.OpenPlanEditor, ElectionAction.AskCandidates, ElectionAction.ToMain],
            KingElection.Rows(ElectionPage.Election).Select(r => r.Action));
        Assert.Equal([ElectionAction.OpenPlanEditor, ElectionAction.AskPlans, ElectionAction.Back],
            KingElection.Rows(ElectionPage.Board).Select(r => r.Action));
        Assert.Equal([ElectionAction.Propose, ElectionAction.AskSenatorBallot, ElectionAction.Back],
            KingElection.Rows(ElectionPage.Proposal).Select(r => r.Action));
    }

    [Fact]
    public void PageTextsComeFromTheClient()
    {
        Assert.Equal(11308, KingElection.PageText(ElectionPage.Main));
        Assert.Equal(11311, KingElection.PageText(ElectionPage.Election));
        Assert.Equal(11315, KingElection.PageText(ElectionPage.Board));
        Assert.Equal(11316, KingElection.PageText(ElectionPage.Impeachment));
        Assert.Equal(11317, KingElection.PageText(ElectionPage.Proposal));
        Assert.Equal(11318, KingElection.PageText(ElectionPage.Senators));
        Assert.Equal(11360, KingElection.KingLine("Zeus"));
        Assert.Equal(11361, KingElection.KingLine(""));
    }

    [Fact]
    public void ElectionAndImpeachmentAlwaysGoBackToTheFirstPage()
    {
        var pager = new ElectionPager();
        pager.Go(ElectionPage.Impeachment);
        pager.Go(ElectionPage.Proposal);
        pager.Back();

        Assert.Equal(ElectionPage.Impeachment, pager.Current);
        Assert.Equal(ElectionPage.Main, pager.Previous);
    }

    [Fact]
    public void ClosingThePlanEditorLeavesBackOnTheBoardReturningToTheElectionPage()
    {
        var pager = new ElectionPager();
        pager.Go(ElectionPage.Election);
        pager.Go(ElectionPage.Board);
        pager.Back();

        Assert.Equal(ElectionPage.Election, pager.Current);
    }

    [Fact]
    public void TheScheduleTextDependsOnTheKind()
    {
        Assert.Equal(11309, KingElection.ScheduleText(KingElection.ScheduleElection));
        Assert.Equal(11310, KingElection.ScheduleText(KingElection.ScheduleImpeachment));
        Assert.Equal(11398, KingElection.ScheduleText(KingElection.ScheduleImpeachmentVote));
        Assert.Equal(0, KingElection.ScheduleText(9));
        Assert.False(KingElection.KnownScheduleKind(9));
        Assert.Equal(new object[] { 10, 12, 0, 30, 24, 30 }, KingElection.ScheduleArgs(10, 12, 0, 30));
    }

    [Fact]
    public void TheNominationWindowNamesTheHoursOrSaysThereIsNoElection()
    {
        Assert.Equal((11402, 0, 0), KingElection.NominateHours(KingElection.NoElectionHour));
        Assert.Equal((11314, 20, 19), KingElection.NominateHours(20));
        Assert.Equal((11314, 0, 23), KingElection.NominateHours(0));
        Assert.Equal(KingElection.NoElectionHour, KingElection.NominateWindowHour(KingElection.ScheduleImpeachment, 20));
        Assert.Equal(20, KingElection.NominateWindowHour(KingElection.ScheduleElection, 20));
    }

    [Fact]
    public void ANomineeNeedsANameOfAtMostTwentyLetters()
    {
        Assert.Equal(11362, KingElection.NomineeRefusal(""));
        Assert.Equal(11363, KingElection.NomineeRefusal(new string('a', 21)));
        Assert.Equal(0, KingElection.NomineeRefusal(new string('a', 20)));
    }

    [Fact]
    public void APlanHoldsFiveHundredLetters()
    {
        Assert.Equal(0, KingElection.PlanRefusal(new string('a', 500)));
        Assert.Equal(7651, KingElection.PlanRefusal(new string('a', 501)));
    }

    [Theory]
    [InlineData(KingReply.Nominate, 1, 11367)]
    [InlineData(KingReply.Nominate, -1, 11364)]
    [InlineData(KingReply.Nominate, -4, 11365)]
    [InlineData(KingReply.Nominate, -5, 11366)]
    [InlineData(KingReply.Nominate, -7, 19304)]
    [InlineData(KingReply.Nominate, -100, KingElection.UnknownResult)]
    [InlineData(KingReply.Nominate, -9, 0)]
    [InlineData(KingReply.Withdraw, -2, 11384)]
    [InlineData(KingReply.Withdraw, -3, 11369)]
    [InlineData(KingReply.PlanPosted, -2, 11375)]
    [InlineData(KingReply.PlanRead, -2, 11376)]
    [InlineData(KingReply.Board, -1, 11322)]
    [InlineData(KingReply.Vote, -4, 19302)]
    [InlineData(KingReply.Vote, -5, 30200)]
    [InlineData(KingReply.Vote, -6, KingElection.UnknownResult)]
    [InlineData(KingReply.Propose, -2, 11303)]
    [InlineData(KingReply.Propose, -5, 11401)]
    [InlineData(KingReply.SenatorVote, -2, 11397)]
    [InlineData(KingReply.PublicVote, -1, 11396)]
    [InlineData(KingReply.PublicVote, -2, 30540)]
    [InlineData(KingReply.SenatorBallot, -1, 11397)]
    [InlineData(KingReply.PublicBallot, -1, 11396)]
    [InlineData(KingReply.Treasury, -1, 11300)]
    [InlineData(KingReply.TariffSet, -1, 11300)]
    [InlineData(KingReply.KingItem, 1, 11411)]
    [InlineData(KingReply.KingItem, -1, 11410)]
    [InlineData(KingReply.KingItem, -2, 11409)]
    [InlineData(KingReply.ChannelOnly, 1, 10238)]
    [InlineData(KingReply.ChannelOnly, 0, 0)]
    public void ResultsShowTheClientsOwnTexts(KingReply reply, short result, int text) =>
        Assert.Equal(text, KingElection.ResultText(reply, result));

    [Fact]
    public void SenatorsVotingIsStateTwoOrFour()
    {
        Assert.True(KingElection.SenatorsAreVoting(2));
        Assert.True(KingElection.SenatorsAreVoting(4));
        Assert.False(KingElection.SenatorsAreVoting(1));
    }

    [Fact]
    public void OnlyTheImpeachmentBallotsHaveTwoChoices()
    {
        Assert.True(KingElection.TwoChoices(KingBox.SenatorBallot));
        Assert.True(KingElection.TwoChoices(KingBox.PublicBallot));
        Assert.False(KingElection.TwoChoices(KingBox.Propose));
        Assert.Equal("Rikka, Zeus", KingElection.Senators(["Rikka", "Zeus"]));
        Assert.Equal("Unknown Result -100", KingElection.UnknownResultLine(-100));
    }
}
