using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PotionHealTests
{
    private const int HealthPurchase = 20, ManaPurchase = 21;
    private const int HealthRestored = 720, ManaRestored = 1920;
    private const int ManaShell = 3, HealthPercent = 5;
    private const int InvalidTarget = 99;

    [Theory]
    [InlineData(HealthPurchase, HealthRestored, HealTarget.Hp, HealthRestored)]
    [InlineData(HealthPurchase, HealthRestored, HealTarget.Mp, 0)]
    [InlineData(ManaPurchase, ManaRestored, HealTarget.Mp, ManaRestored)]
    [InlineData(ManaPurchase, ManaRestored, HealTarget.Hp, 0)]
    public void MaestroPotionsRestoreTheirOwnVital(int directType, int amount, int target, int expected)
    {
        Assert.Equal(expected, ItemData.PotionHealFor(MagicType.DotHeal, directType, amount, target));
    }

    [Theory]
    [InlineData(HealTarget.Hp, HealthRestored, HealTarget.Hp, HealthRestored)]
    [InlineData(HealTarget.Hp, HealthRestored, HealTarget.Mp, 0)]
    [InlineData(HealTarget.Mp, ManaRestored, HealTarget.Mp, ManaRestored)]
    [InlineData(HealTarget.Mp, ManaRestored, HealTarget.Hp, 0)]
    public void OrdinaryPotionsKeepTheirRestoration(int directType, int amount, int target, int expected)
    {
        Assert.Equal(expected, ItemData.PotionHealFor(MagicType.DotHeal, directType, amount, target));
    }

    [Theory]
    [InlineData(MagicType.Buff, HealTarget.Hp, HealthRestored, HealTarget.Hp)]
    [InlineData(MagicType.Melee, HealthPurchase, HealthRestored, HealTarget.Hp)]
    [InlineData(MagicType.DotHeal, HealthPurchase, 0, HealTarget.Hp)]
    [InlineData(MagicType.DotHeal, ManaPurchase, -ManaRestored, HealTarget.Mp)]
    [InlineData(MagicType.DotHeal, ManaShell, ManaRestored, HealTarget.Mp)]
    [InlineData(MagicType.DotHeal, HealthPercent, HealthRestored, HealTarget.Hp)]
    [InlineData(MagicType.DotHeal, 0, HealthRestored, 0)]
    [InlineData(MagicType.DotHeal, HealthPurchase, HealthRestored, HealthPurchase)]
    [InlineData(MagicType.DotHeal, ManaPurchase, ManaRestored, InvalidTarget)]
    public void OtherEffectsAndInvalidTargetsAreNotRestorationPotions(int type, int directType, int amount, int target)
    {
        Assert.Equal(0, ItemData.PotionHealFor(type, directType, amount, target));
    }
}
