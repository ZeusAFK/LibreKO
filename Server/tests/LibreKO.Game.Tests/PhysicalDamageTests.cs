using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class PhysicalDamageTests : GameTestBase
{
    private const int Samples = 300;
    private const ushort AttackPower = 50;
    private const byte FullAttackAmount = 100;
    private const short MoradonZone = 21;
    private const int HellBreaker = 149101228;
    private const short HellBreakerFire = 155;
    private const int IceRing = 190000001;
    private const short IceRingCold = 20;
    private const int FireShield = 190000002;
    private const short FireShieldFire = 30;
    private const int PoisonClaw = 190000003;
    private const short PoisonClawPoison = 100;
    private const int SwordKind = 1;
    private const int RingSlot = 9;
    private const int BareSwingFloor = 34;
    private const int BareSwingCeiling = 47;

    [Fact]
    public void AMonsterHitIsEightyFivePercentOfTheBaseDamagePlusUpToThirtyMore()
    {
        using var provider = CreateProvider(_ => { });
        var attacker = CreateAttacker(provider, MoradonZone);
        var gameData = provider.GetRequiredService<IGameDataService>();

        var monster = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 1 };
        var baseDamage = AttackPower * FullAttackAmount * 2 / 240;

        var landed = Roll(() => PhysicalDamageCalculator.Calculate(
            attacker, PhysicalDefender.Of(monster), gameData));

        landed.Should().NotBeEmpty();
        landed.Should().OnlyContain(
            damage => damage >= (int)(0.85f * baseDamage)
                   && damage <= (int)(0.85f * baseDamage + 0.3f * baseDamage),
            "a normal swing lands at 0.85 of the base plus up to 0.3 more");
        landed.Should().Contain(
            damage => damage > (int)(0.85f * baseDamage),
            "the random share has to reach the target sometimes");
    }

    [Fact]
    public void APriestSwingsForTheSameAsEveryoneElse()
    {
        using var provider = CreateProvider(_ => { });
        var gameData = provider.GetRequiredService<IGameDataService>();
        var monster = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 1 };

        var warrior = CreateAttacker(provider, MoradonZone, characterId: 1, classId: 106);
        var priest = CreateAttacker(provider, MoradonZone, characterId: 2, classId: 110);

        var warriorFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            warrior, PhysicalDefender.Of(monster), gameData)).Min();
        var priestFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            priest, PhysicalDefender.Of(monster), gameData)).Min();

        priestFloor.Should().Be(
            warriorFloor,
            "a normal swing has no class branch — only skills do");
    }

    [Fact]
    public void APlayerTargetHalvesTheDamage()
    {
        using var provider = CreateProvider(_ => { });
        var gameData = provider.GetRequiredService<IGameDataService>();

        var attacker = CreateAttacker(provider, MoradonZone, characterId: 1);
        var victim = CreateAttacker(provider, MoradonZone, characterId: 2);

        var monster = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 1 };
        var monsterFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            attacker, PhysicalDefender.Of(monster), gameData)).Min();
        var playerFloor = Roll(() => PhysicalDamageCalculator.Calculate(
            attacker, PhysicalDefender.Of(victim), gameData)).Min();

        playerFloor.Should().Be(monsterFloor / 2);
    }

    [Fact]
    public void BlockingPhysicalDamageStopsTheSwingOutright()
    {
        using var provider = CreateProvider(_ => { });
        var gameData = provider.GetRequiredService<IGameDataService>();

        var attacker = CreateAttacker(provider, MoradonZone, characterId: 1);
        var victim = CreateAttacker(provider, MoradonZone, characterId: 2);
        victim.BlockPhysical = true;

        PhysicalDamageCalculator.Calculate(attacker, PhysicalDefender.Of(victim), gameData)
            .Should().Be(0);
    }

    [Fact]
    public void AFireWeaponAddsItsFireToASwingOnAMonster()
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone);
        Equip(attacker, InventoryConstants.RightHand, HellBreaker);
        var monster = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 1 };

        var hits = Roll(() => PhysicalDamageCalculator.Calculate(attacker, PhysicalDefender.Of(monster), gameData));

        hits.Should().OnlyContain(damage => damage == HellBreakerFire
            || (damage >= BareSwingFloor + HellBreakerFire && damage <= BareSwingCeiling + HellBreakerFire));
        hits.Should().Contain(damage => damage >= BareSwingFloor + HellBreakerFire);
    }

    [Theory]
    [InlineData(0, 155)]
    [InlineData(50, 117)]
    [InlineData(250, 0)]
    public void AMonstersFireResistanceTrimsTheWeaponFire(short fireResistance, int element)
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone);
        Equip(attacker, InventoryConstants.RightHand, HellBreaker);

        var hit = ItemCombatBonus.Of(attacker, ElementResistances.Of(new NpcInstance { FireR = fireResistance }), gameData);

        hit.Element.Should().Be(element);
    }

    [Fact]
    public void AWeaponElementOnAPlayerIsResistedThenHalvedWithTheSwing()
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone, characterId: 1);
        Equip(attacker, InventoryConstants.RightHand, HellBreaker);
        var victim = CreateAttacker(provider, MoradonZone, characterId: 2);
        victim.Stats.FireR = 40;
        victim.Stats.ResistanceBonus = 10;
        const int resistedFire = 117;

        var hits = Roll(() => PhysicalDamageCalculator.Calculate(attacker, PhysicalDefender.Of(victim), gameData));

        hits.Should().OnlyContain(damage => damage == resistedFire / 2
            || (damage >= (BareSwingFloor + resistedFire) / 2 && damage <= (BareSwingCeiling + resistedFire) / 2));
    }

    [Fact]
    public void RingAndShieldElementsCountWhileDisabledWeaponsLoseTheirs()
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone);
        Equip(attacker, InventoryConstants.RightHand, HellBreaker);
        Equip(attacker, InventoryConstants.LeftHand, FireShield);
        Equip(attacker, RingSlot, IceRing);
        var monster = ElementResistances.Of(new NpcInstance());

        ItemCombatBonus.Of(attacker, monster, gameData).Element
            .Should().Be(HellBreakerFire + FireShieldFire + IceRingCold);

        attacker.WeaponsDisabled = true;
        ItemCombatBonus.Of(attacker, monster, gameData).Element
            .Should().Be(FireShieldFire + IceRingCold);
    }

    [Fact]
    public void APoisonWeaponIsCutOnlyByTheResistanceBonus()
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone);
        Equip(attacker, InventoryConstants.RightHand, PoisonClaw);

        var hit = ItemCombatBonus.Of(attacker, new ElementResistances(Fire: 0, Cold: 0, Lightning: 0, Bonus: 50), gameData);

        hit.Element.Should().Be(PoisonClawPoison - PoisonClawPoison * 50 / 200);
    }

    [Fact]
    public void AnExpiredWeaponCarriesNoElement()
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone);
        Equip(attacker, InventoryConstants.RightHand, HellBreaker);
        attacker.Inventory[InventoryConstants.RightHand].ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        Equip(attacker, InventoryConstants.LeftRing, IceRing);

        var hit = ItemCombatBonus.Of(attacker, ElementResistances.Of(new NpcInstance()), gameData);

        hit.Element.Should().Be(IceRingCold, "only the items still in time add their element");
    }

    [Fact]
    public void AMissStillCarriesTheWeaponElement()
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone);
        attacker.Stats.TotalHitrate = 1;
        Equip(attacker, InventoryConstants.RightHand, HellBreaker);
        var dodger = new NpcInstance { IsMonster = true, Hp = 100000, MaxHp = 100000, Ac = 0, EvadeRate = 10000 };

        var hits = Roll(() => PhysicalDamageCalculator.Calculate(attacker, PhysicalDefender.Of(dodger), gameData));

        hits.Should().Contain(HellBreakerFire);
    }

    [Fact]
    public void BlockingPhysicalDamageStopsTheElementToo()
    {
        using var provider = CreateProvider(_ => { }, ElementItems);
        var gameData = provider.GetRequiredService<IGameDataService>();
        var attacker = CreateAttacker(provider, MoradonZone, characterId: 1);
        Equip(attacker, InventoryConstants.RightHand, HellBreaker);
        var victim = CreateAttacker(provider, MoradonZone, characterId: 2);
        victim.BlockPhysical = true;

        PhysicalDamageCalculator.Calculate(attacker, PhysicalDefender.Of(victim), gameData).Should().Be(0);
    }

    private static void ElementItems(IGameDataService gameData)
    {
        gameData.GetItem(HellBreaker).Returns(new ItemData { Num = HellBreaker, Kind = SwordKind, FireDamage = HellBreakerFire });
        gameData.GetItem(FireShield).Returns(new ItemData { Num = FireShield, Kind = (byte)ItemKind.Shield, FireDamage = FireShieldFire });
        gameData.GetItem(IceRing).Returns(new ItemData { Num = IceRing, IceDamage = IceRingCold });
        gameData.GetItem(PoisonClaw).Returns(new ItemData { Num = PoisonClaw, Kind = SwordKind, PoisonDamage = PoisonClawPoison });
    }

    private static void Equip(UserSession session, int slot, int itemId)
    {
        session.Inventory[slot].ItemId = itemId;
        session.Inventory[slot].Count = 1;
        session.Inventory[slot].Durability = 1;
    }

    private static List<int> Roll(Func<int> calculate)
    {
        var landed = new List<int>();
        for (var i = 0; i < Samples; i++)
        {
            var damage = calculate();
            if (damage > 0)
                landed.Add(damage);
        }

        return landed;
    }

    private static UserSession CreateAttacker(
        ServiceProvider provider, short zoneId, int characterId = 900, short classId = 106)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var session = provider.GetRequiredService<SessionManager>()
            .CreateSession(client, characterId, accountId: characterId + 1000);
        session.Name = $"Attacker{characterId}";
        session.ZoneId = (byte)zoneId;
        session.Class = classId;
        session.Hp = 100;
        session.MaxHp = 100;
        session.AttackAmount = FullAttackAmount;
        session.PlayerAttackAmount = FullAttackAmount;
        session.Stats.TotalHit = AttackPower;
        session.Stats.TotalHitrate = 100;
        session.Stats.TotalEvasionrate = 1;
        session.Stats.TotalAc = 0;
        return session;
    }
}
