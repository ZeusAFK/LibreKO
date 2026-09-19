using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class TempleEventRulesTests
{
    [Theory]
    [InlineData(TempleEvent.Chaos, 20)]
    [InlineData(TempleEvent.BorderDefenseWar, 30)]
    [InlineData(TempleEvent.JuraidMountain, 45)]
    public void EachContestRunsForItsOwnLength(TempleEvent contest, int minutes)
    {
        TempleEventRules.DurationSecondsFor(contest).Should().Be(minutes * 60);
    }

    [Fact]
    public void EveryContestTakesEntriesForTenMinutes()
    {
        TempleEventRules.JoinWindowSeconds.Should().Be(10 * 60);
    }

    [Theory]
    [InlineData(TempleEvent.Chaos, ZoneId.ChaosDungeon)]
    [InlineData(TempleEvent.BorderDefenseWar, ZoneId.BorderDefenseWar)]
    [InlineData(TempleEvent.JuraidMountain, ZoneId.JuradMountain)]
    public void EachContestHasItsOwnZone(TempleEvent contest, ZoneId zone)
    {
        TempleEventRules.ZoneFor(contest).Should().Be((byte)zone);
    }

    [Fact]
    public void NoContestMeansNoZoneAndNoClock()
    {
        TempleEventRules.ZoneFor(TempleEvent.None).Should().Be(0);
        TempleEventRules.DurationSecondsFor(TempleEvent.None).Should().Be(0);
    }

    [Fact]
    public void TheThreeContestsDoNotShareALength()
    {
        var lengths = Enum.GetValues<TempleEvent>()
            .Where(contest => contest != TempleEvent.None)
            .Select(TempleEventRules.DurationSecondsFor)
            .ToArray();

        lengths.Should().OnlyHaveUniqueItems();
    }
}
