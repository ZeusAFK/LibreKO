using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class SetItemBonusTests
{
    private const int CospreItemId = 900000000;
    private const byte Level = 60;
    private const byte Strength = 150;

    [Fact]
    public void ACospreSetAddsItsCarryWeightOnTopOfStrengthAndLevel()
    {
        var plain = Calculate(new SetItemData { SetIndex = CospreItemId });
        var withBonus = Calculate(new SetItemData { SetIndex = CospreItemId, MaxWeightBonus = 3000 });

        plain.MaxWeight.Should().Be((Strength + Level) * 50);
        withBonus.MaxWeight.Should().Be(plain.MaxWeight + 3000);
        withBonus.MaxWeightBonus.Should().Be(3000);
    }

    [Fact]
    public void ASetsAttackPercentageRaisesTheAttackPowerItShows()
    {
        var plain = Calculate(new SetItemData { SetIndex = CospreItemId });
        var withBonus = Calculate(new SetItemData { SetIndex = CospreItemId, APBonusPercent = 3 });

        plain.TotalHit.Should().BeGreaterThan(0);
        withBonus.TotalHit.Should().Be((ushort)(plain.TotalHit * 103 / 100));
    }

    [Fact]
    public void TheRewardPercentagesReachTheStats()
    {
        var stats = Calculate(new SetItemData
        {
            SetIndex = CospreItemId,
            XPBonusPercent = 5,
            CoinBonusPercent = 30,
            NPBonus = 2,
        });

        stats.ItemExpBonusPercent.Should().Be(5);
        stats.ItemCoinBonusPercent.Should().Be(30);
        stats.ItemNpBonus.Should().Be(2);
    }

    [Fact]
    public void TwoSetsAddUp()
    {
        var stats = Calculate(
            new SetItemData { SetIndex = CospreItemId, MaxWeightBonus = 1000, XPBonusPercent = 2 },
            new SetItemData { SetIndex = CospreItemId + 1, MaxWeightBonus = 1500, XPBonusPercent = 3 });

        stats.MaxWeightBonus.Should().Be(2500);
        stats.ItemExpBonusPercent.Should().Be(5);
    }

    private static DerivedStats Calculate(params SetItemData[] sets)
    {
        var gameData = Substitute.For<IGameDataService>();
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var index = 0; index < inventory.Length; index++)
            inventory[index] = new ItemSlot();

        for (var index = 0; index < sets.Length; index++)
        {
            var itemId = CospreItemId + index;
            inventory[InventoryConstants.CospreStart + index].ItemId = itemId;
            inventory[InventoryConstants.CospreStart + index].Count = 1;
            gameData.GetItem(itemId).Returns(new ItemData
            {
                Num = itemId,
                Kind = (byte)ItemKind.Cospre,
            });
            gameData.GetSetItem(itemId).Returns(sets[index]);
        }

        return AbilityCalculator.Calculate(
            Level, Strength, stamina: 100, dexterity: 100, intelligence: 100,
            classId: 101, new CoefficientData { Sword = 0.0005 }, inventory, gameData);
    }
}
