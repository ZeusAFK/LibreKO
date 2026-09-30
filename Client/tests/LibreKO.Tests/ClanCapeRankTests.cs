using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ClanCapeRankTests
{
    private const byte Grade5 = 5;
    private const byte Grade3 = 3;
    private const byte Grade1 = 1;

    [Theory]
    [InlineData(ClanTypes.Training, 1)]
    [InlineData(ClanTypes.Promoted, 2)]
    [InlineData(ClanTypes.Accredited5, 3)]
    [InlineData(ClanTypes.Accredited1, 7)]
    [InlineData(ClanTypes.Royal5, 8)]
    [InlineData(ClanTypes.Royal1, 12)]
    [InlineData(0, 1)]
    public void TheCapeLadderRankIsTheClanTypeItself(byte flag, int rank) =>
        Assert.Equal(rank, ClanTypes.Rank(flag));

    [Fact]
    public void ARoyalFirstClassClanBuysEveryCapeWhateverItsPointGrade()
    {
        Assert.True(ClanTypes.MeetsCapeRank(ClanTypes.Royal1, Grade5, 12, 0));
        Assert.True(ClanTypes.MeetsCapeRank(ClanTypes.Royal1, Grade5, 2, Grade1));
    }

    [Fact]
    public void AClanBelowTheCapesRankIsRefused()
    {
        Assert.False(ClanTypes.MeetsCapeRank(ClanTypes.Royal5, Grade1, 9, 0));
        Assert.False(ClanTypes.MeetsCapeRank(ClanTypes.Training, Grade1, 2, Grade1));
    }

    [Fact]
    public void TrainingKnightsCapesReadThePointGradeOnlyForTrainingKnights()
    {
        Assert.False(ClanTypes.MeetsCapeRank(ClanTypes.Promoted, Grade5, 2, Grade3));
        Assert.True(ClanTypes.MeetsCapeRank(ClanTypes.Promoted, Grade3, 2, Grade3));
        Assert.True(ClanTypes.MeetsCapeRank(ClanTypes.Accredited5, Grade5, 2, Grade3));
    }
}
