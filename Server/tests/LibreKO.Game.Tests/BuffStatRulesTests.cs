using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class BuffStatRulesTests
{
    private const int WeaponId = 180000001;
    private const int EarringId = 190000001;
    private const short WarriorClass = 101;
    private const short KarusRogueClass = 102;
    private const short ElMoradWarriorClass = 201;

    [Fact]
    public void StaminaFromAnAccessoryRaisesMaximumHealth()
    {
        var gameData = GameData();
        var bare = Session(WarriorClass);
        bare.RecalculateStats(Coefficient(WarriorClass), gameData);

        var geared = Session(WarriorClass);
        Equip(geared, InventoryConstants.RightEar, EarringId);
        geared.RecalculateStats(Coefficient(WarriorClass), gameData);

        geared.Stats.StaBonus.Should().Be(26);
        geared.MaxHp.Should().Be(AbilityCalculator.CalculateMaxHp(geared.Level, geared.Stamina + 26, WarriorHpCoefficient, 0));
        geared.MaxHp.Should().BeGreaterThan(bare.MaxHp);
    }

    [Fact]
    public void AWeaponScrollKeepsEveryOtherAttackBonus()
    {
        var gameData = GameData();
        var inventory = Session(WarriorClass).Inventory;
        Equip(inventory, InventoryConstants.RightHand, WeaponId);
        var title = new AchievementTitleData { Attack = 200 };

        var plain = AbilityCalculator.Calculate(
            60, 90, 60, 60, 50, WarriorClass, Coefficient(WarriorClass), inventory, gameData, title: title);
        var scrolled = AbilityCalculator.Calculate(
            60, 90, 60, 60, 50, WarriorClass, Coefficient(WarriorClass), inventory, gameData, title: title,
            weaponDamageBonus: 5);

        scrolled.TotalHit.Should().BeGreaterThan(plain.TotalHit);
    }

    [Fact]
    public void ARogueDrawsAttackFromDexterityWhateverItsNation()
    {
        var gameData = GameData();
        var inventory = Session(KarusRogueClass).Inventory;
        Equip(inventory, InventoryConstants.RightHand, WeaponId);

        var dexterous = AbilityCalculator.Calculate(
            60, 60, 60, 160, 50, KarusRogueClass, Coefficient(KarusRogueClass), inventory, gameData);
        var strong = AbilityCalculator.Calculate(
            60, 160, 60, 60, 50, KarusRogueClass, Coefficient(KarusRogueClass), inventory, gameData);

        dexterous.TotalHit.Should().BeGreaterThan(strong.TotalHit);
    }

    [Fact]
    public void AnElMoradWarriorGetsTheDefenceTreeBonus()
    {
        var gameData = GameData();
        var inventory = Session(ElMoradWarriorClass).Inventory;
        var points = new byte[9];
        points[6] = 40;

        var trained = AbilityCalculator.Calculate(
            60, 60, 60, 60, 50, ElMoradWarriorClass, Coefficient(ElMoradWarriorClass), inventory, gameData, points);
        var untrained = AbilityCalculator.Calculate(
            60, 60, 60, 60, 50, ElMoradWarriorClass, Coefficient(ElMoradWarriorClass), inventory, gameData, new byte[9]);

        trained.ResistanceBonus.Should().BeGreaterThan(untrained.ResistanceBonus);
    }

    [Fact]
    public void BattleCryRaisesStatsButNotDefence()
    {
        var gameData = GameData();
        var session = Session(WarriorClass);
        session.RecalculateStats(Coefficient(WarriorClass), gameData);
        var ac = session.Stats.TotalAc;

        session.ActiveBuffs[106781] = Buff(BuffType.BattleCry, ac: 100, sta: 15);
        session.RecalculateStats(Coefficient(WarriorClass), gameData);

        session.Stats.TotalAc.Should().Be(ac);
        session.Stats.StaBonus.Should().Be(15);
    }

    [Fact]
    public void ADaggerDefenceScrollRaisesDaggerDefenceAndNoStat()
    {
        var gameData = GameData();
        var session = Session(WarriorClass);
        session.ActiveBuffs[511561] = Buff(BuffType.AntiDagger, dex: 9);

        session.RecalculateStats(Coefficient(WarriorClass), gameData);

        session.Stats.DaggerR.Should().Be(5);
        session.Stats.DexBonus.Should().Be(0);
    }

    [Fact]
    public void AttackBuffsOfDifferentTypesAddUp()
    {
        var gameData = GameData();
        var session = Session(WarriorClass);
        session.ActiveBuffs[1] = Buff(BuffType.Damage, attack: 120);
        session.ActiveBuffs[2] = Buff(BuffType.AttackSpeedArmor, attack: 120);

        session.RecalculateStats(Coefficient(WarriorClass), gameData);

        session.AttackAmount.Should().Be(140);
    }

    [Fact]
    public void AMagicAttackColumnOnAnAttackBuffLeavesMagicAlone()
    {
        var gameData = GameData();
        var session = Session(WarriorClass);
        session.ActiveBuffs[1] = Buff(BuffType.Damage, attack: 120, magicAttack: 60);

        session.RecalculateStats(Coefficient(WarriorClass), gameData);

        session.MagicAttackAmount.Should().Be(0);
    }

    private static ActiveBuff Buff(
        BuffType type, short ac = 0, short attack = 100, short magicAttack = 100, short sta = 0, short dex = 0) => new()
    {
        BuffType = type,
        BonusAc = ac,
        BonusAcPct = 100,
        BonusAttack = attack,
        BonusMagicAttack = magicAttack,
        BonusSta = sta,
        BonusDex = dex,
        ExpireTicks = DateTime.UtcNow.AddMinutes(10).Ticks,
    };

    private static IGameDataService GameData()
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetItem(WeaponId).Returns(new ItemData
        {
            Num = WeaponId, Kind = 21, Slot = 1, Damage = 80, Duration = 100,
        });
        gameData.GetItem(EarringId).Returns(new ItemData
        {
            Num = EarringId, Kind = 91, Slot = 11, StaB = 26, Duration = 100,
        });
        return gameData;
    }

    private static CoefficientData Coefficient(short classId) => classId == KarusRogueClass
        ? new CoefficientData
        {
            ClassId = classId, ShortSword = 0.00015, Jamadar = 0.00015, Sword = 0.0001, Axe = 0.0001, Club = 0.0001,
            Spear = 0.0001, Pole = 0.0001, Staff = 0.0001, Bow = 0.00015, Hp = 0.0005, Sp = 0.0015, Ac = 1,
            Hitrate = 0.01, Evasionrate = 0.01,
        }
        : new CoefficientData
        {
            ClassId = classId, ShortSword = 0.0001, Jamadar = 0.0001, Sword = 0.00013, Axe = 0.00013, Club = 0.00013,
            Spear = 0.00013, Pole = 0.00013, Staff = 0.0001, Bow = 0.0001, Hp = WarriorHpCoefficient, Sp = 0.0015, Ac = 1,
            Hitrate = 0.01, Evasionrate = 0.01,
        };

    private const double WarriorHpCoefficient = 0.0015;

    private static UserSession Session(short classId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        return new UserSession(client, 1, 1)
        {
            Class = classId,
            Level = 60,
            Strength = 60,
            Stamina = 60,
            Dexterity = 60,
            Intelligence = 50,
        };
    }

    private static void Equip(UserSession session, int slot, int itemId) => Equip(session.Inventory, slot, itemId);

    private static void Equip(ItemSlot[] inventory, int slot, int itemId)
    {
        inventory[slot].ItemId = itemId;
        inventory[slot].Durability = 100;
        inventory[slot].Count = 1;
    }
}
