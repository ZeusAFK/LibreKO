using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class WeaponTrailRuleTests
{
    [Theory]
    [InlineData(0, 0, 0, 0, WeaponTrailRule.Normal)]
    [InlineData(10, 0, 0, 0, WeaponTrailRule.Fire)]
    [InlineData(0, 8, 0, 0, WeaponTrailRule.Ice)]
    [InlineData(0, 0, 8, 0, WeaponTrailRule.Lightning)]
    [InlineData(0, 0, 0, 8, WeaponTrailRule.Poison)]
    public void EachElementPicksItsOwnTrail(int fire, int ice, int lightning, int poison, int element) =>
        Assert.Equal(element, WeaponTrailRule.Element(fire, ice, lightning, poison));

    [Theory]
    [InlineData(1, 200, 0, 0, WeaponTrailRule.Fire)]
    [InlineData(0, 1, 90, 0, WeaponTrailRule.Ice)]
    [InlineData(0, 0, 1, 255, WeaponTrailRule.Lightning)]
    public void TheFirstElementInTableOrderWinsWhateverTheAmounts(int fire, int ice, int lightning, int poison, int element) =>
        Assert.Equal(element, WeaponTrailRule.Element(fire, ice, lightning, poison));

    [Fact]
    public void AnyNonZeroAmountCountsWithoutAGate() =>
        Assert.Equal(WeaponTrailRule.Poison, WeaponTrailRule.Element(0, 0, 0, 1));

    [Fact]
    public void AFullAfterimageFadesOutInUnderASecond() =>
        Assert.InRange(1f / WeaponTrailRule.AfterimageFadePerSecond, 0.70f, 0.72f);
}
