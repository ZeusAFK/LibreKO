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
    public void ANovaOnAPlayerIsCutToAThirdWithTheTargetClassScale()
    {
        var mage = Mage(0);
        var rogue = new UserSession(Substitute.For<IClient>(), 2, 2) { Class = KarusRogue };

        var hits = Enumerable.Range(0, Samples)
            .Select(_ => MagicCombatHelper.GetMagicDamage(mage, rogue, FlameNovaDamage, Fire, GameData))
            .ToList();

        hits.Should().OnlyContain(hit => hit >= 884 && hit <= 925);
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
