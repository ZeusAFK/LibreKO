using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class UpgradeBagChangesTests
{
    private const byte Normal = 1;
    private const byte Preview = 2;
    private const byte Failed = 0;
    private const byte Succeeded = 1;
    private const byte NeedCoins = 3;
    private const int Raptor7 = 156210007;
    private const int Raptor8 = 156210008;
    private const int Raptor9 = 156210009;
    private const int Scroll = 379021000;
    private const short Raptor9Durability = 15000;
    private const short Raptor8Durability = 7000;
    private const ItemFlag Raptor8State = ItemFlag.NotBound;
    private const int Destroyed = 0;
    private const int ItemAbs = InventoryConstants.InventoryStart;
    private const int ScrollAbs = InventoryConstants.InventoryStart + 1;

    private static ItemSlot[] Bag()
    {
        var bag = new ItemSlot[InventoryConstants.InventoryTotal];
        bag[ItemAbs] = new ItemSlot { ItemId = Raptor8, Count = 1, Durability = Raptor8Durability, Flag = (byte)Raptor8State };
        bag[ScrollAbs] = new ItemSlot { ItemId = Scroll, Count = 3, Durability = 1 };
        return bag;
    }

    private static UpgradeSlotResult[] Reply(int resultItem)
    {
        var slots = new UpgradeSlotResult[10];
        slots[0] = new UpgradeSlotResult(resultItem, 0);
        slots[1] = new UpgradeSlotResult(Scroll, 1);
        for (int i = 2; i < slots.Length; i++) slots[i] = new UpgradeSlotResult(0, -1);
        return slots;
    }

    private static System.Collections.Generic.List<(int Abs, ItemSlot Slot)> Changes(byte type, byte result, int resultItem) =>
        UpgradeBagChanges.For(type, result, Reply(resultItem), Bag(), _ => Raptor9Durability);

    [Fact]
    public void ASucceededUpgradeReplacesTheItemAndUsesOneScroll()
    {
        var changes = Changes(Normal, Succeeded, Raptor9);
        Assert.Equal(2, changes.Count);
        Assert.Equal((ItemAbs, Raptor9, Raptor9Durability), (changes[0].Abs, changes[0].Slot.ItemId, changes[0].Slot.Durability));
        Assert.Equal(Raptor8State, changes[0].Slot.State);
        Assert.Equal((ScrollAbs, Scroll, (short)2), (changes[1].Abs, changes[1].Slot.ItemId, changes[1].Slot.Count));
    }

    [Fact]
    public void AFailedUpgradeDestroysTheItemAndUsesOneScroll()
    {
        var changes = Changes(Normal, Failed, Destroyed);
        Assert.True(changes[0].Slot.IsEmpty);
        Assert.Equal((short)2, changes[1].Slot.Count);
    }

    [Fact]
    public void ARefusedUpgradeLeavesTheBagAlone() => Assert.Empty(Changes(Normal, NeedCoins, Raptor8));

    [Fact]
    public void APreviewLeavesTheBagAlone() => Assert.Empty(Changes(Preview, Succeeded, Raptor9));

    [Theory]
    [InlineData(Raptor8)]
    [InlineData(Raptor7)]
    public void AProtectedFailureKeepsTheItemStateAndDurabilityAndConsumesOneMaterial(int returned)
    {
        var changes = Changes(Normal, Failed, returned);
        Assert.Equal((ItemAbs, returned, Raptor8Durability), (changes[0].Abs, changes[0].Slot.ItemId, changes[0].Slot.Durability));
        Assert.Equal(Raptor8State, changes[0].Slot.State);
        Assert.True(changes[0].Slot.IsTradable);
        Assert.Equal((ScrollAbs, (short)2), (changes[1].Abs, changes[1].Slot.Count));
    }
}
