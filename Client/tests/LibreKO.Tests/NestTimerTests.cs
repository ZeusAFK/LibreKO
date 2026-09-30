using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NestTimerTests
{
    [Theory]
    [InlineData(1786, "Mission termination time....00:29:46")]
    [InlineData(59, "Mission termination time....00:00:59")]
    [InlineData(3600, "Mission termination time....01:00:00")]
    public void TheRunningNestShowsItsTimeAsHoursMinutesSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, NestDungeon.TerminationLine(seconds, completed: false));
    }

    [Fact]
    public void ACompletedNestCountsDownInSeconds()
    {
        Assert.Equal("Mission termination time.... 11 seconds", NestDungeon.TerminationLine(11, completed: true));
    }

    [Theory]
    [InlineData(81, true)]
    [InlineData(83, true)]
    [InlineData(84, false)]
    [InlineData(21, false)]
    public void OnlyTheThreeNestsAreNestZones(int zone, bool nest)
    {
        Assert.Equal(nest, NestDungeon.IsNestZone(zone));
    }
}
