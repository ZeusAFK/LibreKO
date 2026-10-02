using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class WeaponGlowRuleTests
{
    private const int RaptorKind = 52;
    private const int RaptorEffect2 = 12070000;
    private const int PoisonSword = 12031;

    [Fact]
    public void AnElementalRowOfAWeaponWithAnOwnGlowTakesTheWeaponGlow() =>
        Assert.Equal(12070, WeaponGlowRule.VariantFx(PoisonSword, RaptorKind, RaptorEffect2, 0, 0, 0, 0));

    [Fact]
    public void ARowThatBecomesAnotherItemKeepsItsOwnGlow() =>
        Assert.Equal(WeaponGlowRule.NoFx,
            WeaponGlowRule.VariantFx(PoisonSword, RaptorKind, RaptorEffect2, 156210000, 0, 0, 0));

    [Fact]
    public void ARowWithoutAGlowNeverTakesTheWeaponGlow() =>
        Assert.Equal(WeaponGlowRule.NoFx, WeaponGlowRule.VariantFx(0, RaptorKind, RaptorEffect2, 0, 0, 0, 0));

    [Fact]
    public void AWeaponWithoutAnOwnGlowKeepsTheRowGlow() =>
        Assert.Equal(WeaponGlowRule.NoFx, WeaponGlowRule.VariantFx(PoisonSword, RaptorKind, 0, 0, 0, 0, 0));

    [Theory]
    [InlineData(64, 0, 0, 12000)]
    [InlineData(0, 64, 0, 12010)]
    [InlineData(10, 64, 64, 12010)]
    [InlineData(64, 64, 64, 12000)]
    [InlineData(0, 10, 64, 12020)]
    [InlineData(0, 0, 0, WeaponGlowRule.NoFx)]
    public void AnElementalKindPicksTheGlowOfItsStrongestElement(int fire, int ice, int lightning, int fxId) =>
        Assert.Equal(fxId, WeaponGlowRule.VariantFx(PoisonSword, WeaponGlowRule.ElementalKind, 12000000, 7, fire, ice, lightning));

    [Theory]
    [InlineData(5, 64, 0, 0, 0, WeaponGlowRule.FireTint)]
    [InlineData(5, 0, 64, 0, 0, WeaponGlowRule.IceTint)]
    [InlineData(5, 0, 0, 64, 0, WeaponGlowRule.LightningTint)]
    [InlineData(5, 0, 0, 0, 70, WeaponGlowRule.PoisonTint)]
    [InlineData(5, 0, 56, 0, 0, WeaponGlowRule.White)]
    [InlineData(4, 0, 0, 0, 12, WeaponGlowRule.PoisonTint)]
    [InlineData(12, 0, 1, 0, 0, WeaponGlowRule.IceTint)]
    [InlineData(5, 0, 0, 0, 0, WeaponGlowRule.White)]
    public void TheTintFollowsTheFirstElementPastItsGate(int grade, int fire, int ice, int lightning, int poison, uint tint) =>
        Assert.Equal(tint, WeaponGlowRule.Tint(grade, fire, ice, lightning, poison));
}
