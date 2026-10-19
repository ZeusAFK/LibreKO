using LibreKO.Domain;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class PotionSkillTests
{
    private const int PotionItemGroup = 9;
    private const int CorpsMoral = 32;
    private const int Durability = 4;
    private const double Now = 1000.0;
    private const double MpPressedAfter = 0.05;

    private static SkillData.Skill Restore(int id, int useItem, int directType, int firstDamage, float cast, float recast) => new()
    {
        Id = id,
        Type1 = MagicType.DotHeal,
        Moral = SkillTarget.Self,
        ItemGroup = PotionItemGroup,
        UseItem = useItem,
        Cast = cast,
        Recast = recast,
        DirectType = directType,
        FirstDamage = firstDamage,
    };

    private static SkillData.Skill HolyWater() => Restore(490010, 389010000, HealTarget.Hp, 45, cast: 5, recast: 20);

    private static SkillData.Skill PotionOfSpirit() => Restore(490016, 389016000, HealTarget.Mp, 120, cast: 5, recast: 20);

    private static SkillData.Skill RiceCake() => Restore(490001, 389001000, HealTarget.Hp, 200, cast: 0, recast: 0);

    private static SkillData.Skill GreenRiceCake() => Restore(490004, 389004000, HealTarget.Mp, 200, cast: 0, recast: 0);

    private static SkillData.Skill HyperHealing()
    {
        var scroll = Restore(460006, 310310010, HealTarget.Hp, 0, cast: 15, recast: 3600);
        scroll.TimeDamage = 50;
        scroll.Duration = 60;
        return scroll;
    }

    [Fact]
    public void HpAndMpPotionsWithACastTimeArePotions()
    {
        Assert.True(HolyWater().IsPotion);
        Assert.True(PotionOfSpirit().IsPotion);
    }

    [Fact]
    public void RiceCakesArePotions()
    {
        Assert.True(RiceCake().IsPotion);
        Assert.True(GreenRiceCake().IsPotion);
    }

    [Fact]
    public void MaestroPotionsArePotionsDrunkFromTheMaestroItem()
    {
        var recoverHealth = Restore(490076, 0, HealTarget.HpPurchase, 720, cast: 5, recast: 20);
        var manaRecovery = Restore(490084, 0, HealTarget.MpPurchase, 1920, cast: 5, recast: 20);

        Assert.True(recoverHealth.IsPotion);
        Assert.True(manaRecovery.IsPotion);
        Assert.Equal(MaestroPotions.Health, recoverHealth.PotionItem);
        Assert.Equal(MaestroPotions.Mana, manaRecovery.PotionItem);
    }

    [Fact]
    public void APotionIsDrunkFromItsOwnItem()
    {
        Assert.Equal(HolyWater().UseItem, HolyWater().PotionItem);
    }

    [Fact]
    public void AClassSkillThatHealsItsCasterIsNotAPotion()
    {
        var returnToLife = Restore(105740, 0, HealTarget.Hp, 720, cast: 0, recast: 51);

        Assert.False(returnToLife.IsPotion);
        Assert.Equal(0, returnToLife.PotionItem);
    }

    [Fact]
    public void AHealOverTimeScrollIsNotAPotion()
    {
        Assert.False(HyperHealing().IsPotion);
    }

    [Fact]
    public void AMagicHammerIsNotAPotion()
    {
        Assert.False(Restore(490153, 389153000, Durability, 2000, cast: 5, recast: 25).IsPotion);
    }

    [Fact]
    public void AHealThatReachesThePartyIsNotAPotion()
    {
        var partyHeal = Restore(490406, 389406000, HealTarget.Hp, 720, cast: 5, recast: 50);
        partyHeal.Moral = CorpsMoral;

        Assert.False(partyHeal.IsPotion);
    }

    [Fact]
    public void AnHpPotionHoldsTheMpPotionUntilTheSharedCooldownEnds()
    {
        var timers = new SkillTimers(new SelfSession());

        timers.Start(HolyWater(), Now);

        Assert.True(timers.OnCooldown(PotionOfSpirit(), Now + MpPressedAfter));
        Assert.False(timers.OnCooldown(PotionOfSpirit(), Now + SkillTimers.PotionCooldownSeconds));
    }

    [Fact]
    public void ARiceCakeHoldsHolyWater()
    {
        var timers = new SkillTimers(new SelfSession());

        timers.Start(RiceCake(), Now);

        Assert.True(timers.OnCooldown(HolyWater(), Now + MpPressedAfter));
    }

    [Fact]
    public void AMaestroHpPotionHoldsTheMpPotion()
    {
        var timers = new SkillTimers(new SelfSession());

        timers.Start(Restore(490076, 0, HealTarget.HpPurchase, 720, cast: 5, recast: 20), Now);

        Assert.True(timers.OnCooldown(PotionOfSpirit(), Now + MpPressedAfter));
    }

    [Fact]
    public void APotionDoesNotHoldAHealOverTimeScroll()
    {
        var timers = new SkillTimers(new SelfSession());

        timers.Start(HolyWater(), Now);

        Assert.False(timers.OnCooldown(HyperHealing(), Now + MpPressedAfter));
    }
}
