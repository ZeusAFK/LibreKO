using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class PriestAttackPowerTests
{
    private const short MasterPriest = 112;
    private const byte Level = 83;
    private const byte LowStat = 60;
    private const byte HighStat = 200;
    private const int Mace = 191_210_001;
    private const int Club = 191_110_001;
    private const short WeaponAttack = 90;

    [Fact]
    public void APriestWithAMaceDrawsAttackPowerFromIntelligence()
    {
        var maceWithIntelligence = Calculate(Mace, strength: LowStat, intelligence: HighStat);
        var clubWithStrength = Calculate(Club, strength: HighStat, intelligence: LowStat);

        maceWithIntelligence.TotalHit.Should().Be(clubWithStrength.TotalHit, "a mace swaps strength for intelligence");
    }

    [Fact]
    public void APriestWithAnotherWeaponStillUsesStrength()
    {
        var withIntelligence = Calculate(Club, strength: LowStat, intelligence: HighStat);
        var withStrength = Calculate(Club, strength: HighStat, intelligence: LowStat);

        withStrength.TotalHit.Should().BeGreaterThan(withIntelligence.TotalHit);
    }

    private static DerivedStats Calculate(int weapon, byte strength, byte intelligence)
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetItem(Mace).Returns(new ItemData { Num = Mace, Kind = (byte)ItemKind.Mace, Damage = WeaponAttack });
        gameData.GetItem(Club).Returns(new ItemData { Num = Club, Kind = (byte)ItemKind.ClubOneHand, Damage = WeaponAttack });
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var index = 0; index < inventory.Length; index++)
            inventory[index] = new ItemSlot();
        inventory[InventoryConstants.RightHand].ItemId = weapon;
        inventory[InventoryConstants.RightHand].Count = 1;
        inventory[InventoryConstants.RightHand].Durability = 1;

        return AbilityCalculator.Calculate(
            Level, strength, stamina: 100, dexterity: 60, intelligence: intelligence,
            classId: MasterPriest, new CoefficientData { Club = 0.00025, Pole = 0.00025 }, inventory, gameData);
    }
}
