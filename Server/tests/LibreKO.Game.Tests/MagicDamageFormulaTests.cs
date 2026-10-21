using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MagicDamageFormulaTests
{
    private const short KarusMage = 110;
    private const short KarusRogue = 108;
    private const byte MageLevel = 79;
    private const byte Charisma = 255;
    private const short CharismaBonus = 14;
    private const int FlameNovaDamage = 1800;
    private const byte Fire = (byte)MagicAttribute.Fire;
    private const int Lobo = 180_110_278;
    private const int Erenion = 189_111_278;
    private const int Samples = 2_000;
    private const short KarusWarrior = 101;
    private const short KarusPriest = 104;
    private const int IceCometDamage = 882;
    private const byte Cold = (byte)MagicAttribute.Cold;
    private const int ApostleAttack = 3152;
    private const short ApostleEvasion = 85;

    private static readonly IGameDataService GameData = BuildGameData();

    [Theory]
    [InlineData(0, 2933, 3073)]
    [InlineData(Lobo, 3083, 3223)]
    [InlineData(Erenion, 3620, 3760)]
    public void ANovaOnAMonsterLandsInsideTheFormulasRange(int staff, int lowest, int highest)
    {
        var mage = Mage(staff);
        var monster = new NpcInstance { FireR = 0 };

        var hits = Enumerable.Range(0, Samples)
            .Select(_ => MagicCombatHelper.GetMagicDamage(mage, monster, FlameNovaDamage, Fire, GameData))
            .ToList();

        hits.Should().OnlyContain(hit => hit >= lowest && hit <= highest);
        hits.Min().Should().BeLessThan(lowest + (highest - lowest) / 4, "the roll covers the low end");
        hits.Max().Should().BeGreaterThan(highest - (highest - lowest) / 4, "the roll covers the high end");
    }

    [Fact]
    public void AnExpiredStaffAddsNothingToANova()
    {
        var mage = Mage(Erenion);
        mage.Inventory[InventoryConstants.RightHand].ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        var monster = new NpcInstance { FireR = 0 };

        var hits = Enumerable.Range(0, Samples)
            .Select(_ => MagicCombatHelper.GetMagicDamage(mage, monster, FlameNovaDamage, Fire, GameData))
            .ToList();

        hits.Should().OnlyContain(hit => hit >= 2933 && hit <= 3073, "a staff that ran out casts like no staff");
    }

    [Fact]
    public void ANovaOnAPlayerIsCutToAThirdWithTheTargetClassScale()
    {
        var mage = Mage(0);
        var rogue = new UserSession(Substitute.For<IClient>(), 2, 2) { Class = KarusRogue };

        var hits = Enumerable.Range(0, Samples)
            .Select(_ => MagicCombatHelper.GetMagicDamage(mage, rogue, FlameNovaDamage, Fire, GameData))
            .ToList();

        hits.Should().OnlyContain(hit => hit >= 884 && hit <= 925);
    }

    [Theory]
    [InlineData(KarusWarrior, 0, 217, 230)]
    [InlineData(KarusWarrior, 150, 168, 178)]
    [InlineData(KarusMage, 0, 220, 233)]
    [InlineData(KarusPriest, 0, 195, 207)]
    public void AMonsterIceCometIgnoresTheMonstersAttackAndUsesTheTargetsResistance(
        short targetClass, short coldResistance, int lowest, int highest)
    {
        var apostle = new NpcInstance { Attack1 = ApostleAttack, EvadeRate = ApostleEvasion };
        var target = new UserSession(Substitute.For<IClient>(), 3, 3) { Class = targetClass };
        target.Stats.ColdR = coldResistance;

        var hits = Enumerable.Range(0, Samples)
            .Select(_ => MagicCombatHelper.GetMagicDamage(apostle, target, IceCometDamage, Cold))
            .ToList();

        hits.Should().OnlyContain(hit => hit == 0 || (hit >= lowest && hit <= highest));
        hits.Should().Contain(hit => hit >= lowest, "most casts land");
        hits.Should().Contain(0, "a monster spell can miss");
    }

    [Fact]
    public void AMonsterSpellOnATargetThatBlocksMagicDoesNothing()
    {
        var apostle = new NpcInstance { Attack1 = ApostleAttack, EvadeRate = ApostleEvasion };
        var target = new UserSession(Substitute.For<IClient>(), 4, 4) { Class = KarusWarrior, BlockMagic = true };

        Enumerable.Range(0, Samples)
            .Select(_ => MagicCombatHelper.GetMagicDamage(apostle, target, IceCometDamage, Cold))
            .Should().OnlyContain(hit => hit == 0);
    }

    private static UserSession Mage(int staff)
    {
        var mage = new UserSession(Substitute.For<IClient>(), 1, 1)
        {
            Class = KarusMage,
            Level = MageLevel,
            Magic = Charisma,
        };
        mage.Stats.ChaBonus = CharismaBonus;
        if (staff != 0)
        {
            mage.Inventory[InventoryConstants.RightHand].ItemId = staff;
            mage.Inventory[InventoryConstants.RightHand].Count = 1;
        }
        return mage;
    }

    private static IGameDataService BuildGameData()
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetItem(Lobo).Returns(new ItemData { Num = Lobo, Kind = (byte)ItemKind.Staff, Damage = 71, IceDamage = 66 });
        gameData.GetItem(Erenion).Returns(new ItemData { Num = Erenion, Kind = (byte)ItemKind.Staff, Damage = 106, FireDamage = 90 });
        return gameData;
    }
}
