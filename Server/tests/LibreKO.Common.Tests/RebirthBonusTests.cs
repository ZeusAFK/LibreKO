using FluentAssertions;
using LibreKO.Common.Domain.Services;

namespace LibreKO.Common.Tests;

public class RebirthBonusTests
{
    [Fact]
    public void EachRebirthIsWorthTwoPoints()
    {
        RebirthBonus.PointsFor(0).Should().Be(0);
        RebirthBonus.PointsFor(1).Should().Be(2);
        RebirthBonus.PointsFor(10).Should().Be(20);
    }

    [Fact]
    public void PointsStopAtTheRebirthCap()
    {
        RebirthBonus.PointsFor(11).Should().Be(RebirthBonus.PointsFor(RebirthBonus.MaxRebirthLevel));
        RebirthBonus.PointsFor(-3).Should().Be(0);
    }

    [Fact]
    public void EachRebirthMakesTheNextOneCostMore()
    {
        const long levelExp = 1_000_000;

        RebirthBonus.RequiredExperience(levelExp, 0).Should().Be(levelExp);
        RebirthBonus.RequiredExperience(levelExp, 1).Should().Be(2 * levelExp);
        RebirthBonus.RequiredExperience(levelExp, 9).Should().Be(10 * levelExp);
    }

    [Fact]
    public void ACharacterThatHasNeverRebirthedIsUnaffected()
    {
        RebirthBonus.RequiredExperience(12_345, -1).Should().Be(12_345);
        RebirthBonus.None.IsEmpty.Should().BeTrue();
        RebirthBonus.None.Total.Should().Be(0);
    }

    [Fact]
    public void TheBonusCountsEveryStatItCarries()
    {
        new RebirthBonus(3, 1, 0, 4, 2).Total.Should().Be(10);
        new RebirthBonus(3, 1, 0, 4, 2).IsEmpty.Should().BeFalse();
    }
}
