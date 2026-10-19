using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ItemStackMergeTests
{
    private const int Potion = 389010000;
    private const int OtherPotion = 389011000;
    private const int Stackable = 1;
    private const int Single = 0;

    private static ItemSlot Stack(int itemId, int count, byte flag = 0, int uniqueId = 0) =>
        new() { ItemId = itemId, Count = (short)count, Flag = flag, UniqueId = uniqueId };

    [Theory]
    [InlineData((byte)ItemMoveDirection.InventoryToInventory)]
    [InlineData((byte)ItemMoveDirection.InventoryToMagicBag)]
    [InlineData((byte)ItemMoveDirection.MagicBagToInventory)]
    [InlineData((byte)ItemMoveDirection.MagicBagToMagicBag)]
    public void TheSameStackableItemMergesBetweenBagsAndMagicBags(byte direction) =>
        Assert.True(ItemMove.Merges((ItemMoveDirection)direction, Stack(Potion, 3), Stack(Potion, 7), Stackable));

    [Theory]
    [InlineData((byte)ItemMoveDirection.InventoryToSlot)]
    [InlineData((byte)ItemMoveDirection.InventoryToCospre)]
    [InlineData((byte)ItemMoveDirection.InventoryToBagSlot)]
    public void EquippingNeverMerges(byte direction) =>
        Assert.False(ItemMove.Merges((ItemMoveDirection)direction, Stack(Potion, 3), Stack(Potion, 7), Stackable));

    [Fact]
    public void OnlyTheSameStackableItemWithRoomMerges()
    {
        Assert.False(ItemMove.Merges(ItemMoveDirection.InventoryToMagicBag, Stack(Potion, 1), Stack(Potion, 1), Single));
        Assert.False(ItemMove.Merges(ItemMoveDirection.InventoryToMagicBag, Stack(Potion, 1), Stack(OtherPotion, 1), Stackable));
        Assert.False(ItemMove.Merges(ItemMoveDirection.InventoryToMagicBag, Stack(Potion, 20), Stack(Potion, 9990), Stackable));
        Assert.True(ItemMove.Merges(ItemMoveDirection.InventoryToMagicBag, Stack(Potion, 9), Stack(Potion, 9990), Stackable));
    }

    [Fact]
    public void SealedAndLinkedItemsKeepTheirOwnSlot()
    {
        const byte sealedFlag = (byte)ItemFlag.Sealed;
        Assert.False(ItemMove.Merges(ItemMoveDirection.MagicBagToMagicBag, Stack(Potion, 1, sealedFlag), Stack(Potion, 1), Stackable));
        Assert.False(ItemMove.Merges(ItemMoveDirection.MagicBagToMagicBag, Stack(Potion, 1, uniqueId: 5), Stack(Potion, 1), Stackable));
    }

    [Fact]
    public void MergingFillsTheTargetAndEmptiesTheSource()
    {
        var inv = new Inventory();
        int from = InventoryConstants.InventoryStart, to = InventoryConstants.MagicBagStart;
        inv.ApplySlotUpdate(from, Stack(Potion, 3));
        inv.ApplySlotUpdate(to, Stack(Potion, 7));

        inv.Stack(to, inv[from].Count);
        inv.Consume(from, inv[from].Count);

        Assert.Equal(10, inv[to].Count);
        Assert.True(inv[from].IsEmpty);
    }
}
