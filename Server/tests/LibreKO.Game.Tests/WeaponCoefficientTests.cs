using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class WeaponCoefficientTests
{
    private const int WeaponItemId = 4242;
    private const short TestClassId = 106;
    private const double Marker = 0.001;

    public static TheoryData<ItemKind, string> WeaponColumns() => new()
    {
        { ItemKind.Dagger, nameof(CoefficientData.ShortSword) },
        { ItemKind.Jamadhar, nameof(CoefficientData.Jamadar) },
        { ItemKind.SwordOneHand, nameof(CoefficientData.Sword) },
        { ItemKind.SwordTwoHand, nameof(CoefficientData.Sword) },
        { ItemKind.AxeOneHand, nameof(CoefficientData.Axe) },
        { ItemKind.AxeTwoHand, nameof(CoefficientData.Axe) },
        { ItemKind.ClubOneHand, nameof(CoefficientData.Club) },
        { ItemKind.ClubTwoHand, nameof(CoefficientData.Club) },
        { ItemKind.SpearOneHand, nameof(CoefficientData.Spear) },
        { ItemKind.SpearTwoHand, nameof(CoefficientData.Spear) },
        { ItemKind.Mace, nameof(CoefficientData.Pole) },
        { ItemKind.Staff, nameof(CoefficientData.Staff) },
        { ItemKind.Bow, nameof(CoefficientData.Bow) },
        { ItemKind.Crossbow, nameof(CoefficientData.Bow) },
    };

    [Theory]
    [MemberData(nameof(WeaponColumns))]
    public void EachWeaponReadsItsOwnCoefficientColumn(ItemKind kind, string column)
    {
        var withColumn = TotalHitFor(kind, OnlyColumnSet(column));
        var withoutAny = TotalHitFor(kind, AllColumnsZero());

        withColumn.Should().BeGreaterThan(
            withoutAny,
            "a {0} scales its attack power off {1}", kind, column);
    }

    [Theory]
    [MemberData(nameof(WeaponColumns))]
    public void NoWeaponReadsAnyOtherCoefficientColumn(ItemKind kind, string column)
    {
        var withEveryOtherColumn = TotalHitFor(kind, EveryColumnSetExcept(column));
        var withoutAny = TotalHitFor(kind, AllColumnsZero());

        withEveryOtherColumn.Should().Be(
            withoutAny,
            "a {0} must ignore every column but {1}", kind, column);
    }

    private static ushort TotalHitFor(ItemKind kind, CoefficientData coefficient)
    {
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetItem(WeaponItemId).Returns(new ItemData
        {
            Num = WeaponItemId,
            Kind = (byte)kind,
            Damage = 100,
        });

        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var slot = 0; slot < inventory.Length; slot++)
            inventory[slot] = new ItemSlot();
        inventory[InventoryConstants.RightHand] = new ItemSlot { ItemId = WeaponItemId, Count = 1 };

        var stats = AbilityCalculator.Calculate(
            level: 60, strength: 120, stamina: 60, dexterity: 60, intelligence: 60,
            classId: TestClassId, coefficient, inventory, gameData);

        return stats.TotalHit;
    }

    private static CoefficientData AllColumnsZero() => new() { ClassId = TestClassId };

    private static CoefficientData OnlyColumnSet(string column)
    {
        var coefficient = AllColumnsZero();
        typeof(CoefficientData).GetProperty(column)!.SetValue(coefficient, Marker);
        return coefficient;
    }

    private static CoefficientData EveryColumnSetExcept(string column)
    {
        var coefficient = AllColumnsZero();
        foreach (var other in WeaponCoefficientColumns.Where(name => name != column))
            typeof(CoefficientData).GetProperty(other)!.SetValue(coefficient, Marker);

        return coefficient;
    }

    private static readonly string[] WeaponCoefficientColumns =
    [
        nameof(CoefficientData.ShortSword),
        nameof(CoefficientData.Jamadar),
        nameof(CoefficientData.Sword),
        nameof(CoefficientData.Axe),
        nameof(CoefficientData.Club),
        nameof(CoefficientData.Spear),
        nameof(CoefficientData.Pole),
        nameof(CoefficientData.Staff),
        nameof(CoefficientData.Bow),
    ];
}
