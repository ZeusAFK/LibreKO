using FluentAssertions;
using LibreKO.Common.Domain.Services;
using Xunit;

namespace LibreKO.Game.Tests;

public class BaseStatTests
{
    [Theory]
    [InlineData(101, 65, 65, 60, 50, 50)]
    [InlineData(102, 60, 60, 70, 50, 50)]
    [InlineData(103, 50, 50, 70, 70, 50)]
    [InlineData(104, 50, 60, 60, 70, 50)]
    public void BaseStatsMatchTheCharacterCreationRolls(
        int classId, int str, int sta, int dex, int intel, int magic)
    {
        var stats = ProgressionTable.BaseStatsForClass(classId);

        stats.Strength.Should().Be((byte)str);
        stats.Stamina.Should().Be((byte)sta);
        stats.Dexterity.Should().Be((byte)dex);
        stats.Intelligence.Should().Be((byte)intel);
        stats.Magic.Should().Be((byte)magic);
    }

    [Theory]
    [InlineData(101, 105, 106)]
    [InlineData(102, 107, 108)]
    [InlineData(103, 109, 110)]
    [InlineData(104, 111, 112)]
    public void PromotedClassesKeepTheirFamilyBase(int beginner, int novice, int master)
    {
        var expected = ProgressionTable.BaseStatsForClass(beginner);

        ProgressionTable.BaseStatsForClass(novice).Should().Be(expected);
        ProgressionTable.BaseStatsForClass(master).Should().Be(expected);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(202)]
    [InlineData(103)]
    [InlineData(204)]
    [InlineData(113)]
    [InlineData(999)]
    public void EveryBaseSpreadSumsToTheRedistributionTotal(int classId)
    {
        var stats = ProgressionTable.BaseStatsForClass(classId);

        (stats.Strength + stats.Stamina + stats.Dexterity + stats.Intelligence + stats.Magic)
            .Should().Be(ProgressionTable.BaseStatTotal);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(201)]
    public void NationDoesNotChangeTheBaseSpread(int classId)
    {
        ProgressionTable.BaseStatsForClass(classId)
            .Should().Be(ProgressionTable.BaseStatsForClass(101));
    }

    [Theory]
    [InlineData(1, 300)]
    [InlineData(60, 477)]
    [InlineData(61, 482)]
    [InlineData(75, 552)]
    [InlineData(76, 557)]
    [InlineData(83, 592)]
    public void TheGrantedPointsPlusTheBaseSpreadMatchTheKnownTotals(int level, int expected)
    {
        (ProgressionTable.BaseStatTotal + ProgressionTable.StatPointsForLevel(level))
            .Should().Be(expected, "these totals are what characters at each level actually hold, "
                + "and they only add up if the grant rises from three a level to five above level 60");
    }

    [Fact]
    public void EveryLevelPastSixtyGrantsFivePointsRatherThanThree()
    {
        for (var level = ProgressionTable.MinLevel + 1; level <= ProgressionTable.MaxLevel; level++)
        {
            var granted = ProgressionTable.StatPointsForLevel(level)
                - ProgressionTable.StatPointsForLevel(level - 1);

            granted.Should().Be(
                level > ProgressionTable.StatBonusLevel
                    ? ProgressionTable.StatPointsPerLevel + ProgressionTable.BonusStatPointsPerLevel
                    : ProgressionTable.StatPointsPerLevel,
                "level {0} sits {1} the bonus threshold", level,
                level > ProgressionTable.StatBonusLevel ? "above" : "at or below");
        }
    }
}
