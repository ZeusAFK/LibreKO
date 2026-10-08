using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ItemMoveWireTests
{
    private const int Potion = 389010000;
    private const int OtherPotion = 389011000;
    private const int Stackable = 1;
    private const int Single = 0;
    private const int BagSlot = InventoryConstants.MagicBagStart;
    private const int GridSlot = InventoryConstants.InventoryStart;
    private const byte Rented = (byte)ItemFlag.Rented;

    private static ItemSlot Stack(int itemId, int count, byte flag = 0, int uniqueId = 0) =>
        new() { ItemId = itemId, Count = (short)count, Durability = 1, Flag = flag, UniqueId = uniqueId };

    [Fact]
    public void AMoveCarriesItsAmountAfterTheDestination()
    {
        var bytes = ItemMoveWire.Move(ItemMove.MagicBagToInventory, Potion, 3, 7, 25).GetBytes();

        Assert.Equal(new byte[]
        {
            (byte)GameOpcodes.GS_ITEM_MOVE, ItemMove.MoveRequest, ItemMove.MagicBagToInventory,
            0x50, 0xD2, 0x2F, 0x17, 3, 7, 25, 0,
        }, bytes);
    }

    [Fact]
    public void AWholeStackMoveSendsAZeroAmount()
    {
        var bytes = ItemMoveWire.Move(ItemMove.InventoryToSlot, Potion, 0, 6, ItemMove.WholeStack).GetBytes();

        Assert.Equal(new byte[] { 0, 0 }, bytes[^2..]);
    }

    [Theory]
    [InlineData(ItemMove.InventoryToMagicBag)]
    [InlineData(ItemMove.MagicBagToInventory)]
    [InlineData(ItemMove.MagicBagToMagicBag)]
    public void PartOfAStackSplitsBetweenTheBagAndAMagicBag(byte direction)
    {
        Assert.True(ItemMove.Splits(direction, Stack(Potion, 10), default, Stackable, 4));
        Assert.True(ItemMove.Splits(direction, Stack(Potion, 10), Stack(Potion, 5), Stackable, 4));
    }

    [Theory]
    [InlineData(ItemMove.InventoryToInventory)]
    [InlineData(ItemMove.InventoryToSlot)]
    [InlineData(ItemMove.InventoryToPet)]
    public void OtherMovesNeverSplit(byte direction) =>
        Assert.False(ItemMove.Splits(direction, Stack(Potion, 10), default, Stackable, 4));

    [Fact]
    public void OnlyAStackableUnlinkedPartWithRoomSplits()
    {
        Assert.False(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10), default, Single, 4));
        Assert.False(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10, uniqueId: 42), default, Stackable, 4));
        Assert.False(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10), default, Stackable, 10));
        Assert.False(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10), default, Stackable, ItemMove.WholeStack));
        Assert.False(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10), Stack(OtherPotion, 1), Stackable, 4));
        Assert.False(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10), Stack(Potion, 1, flag: Rented), Stackable, 4));
        Assert.False(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10), Stack(Potion, 9996), Stackable, 4));
        Assert.True(ItemMove.Splits(ItemMove.InventoryToMagicBag, Stack(Potion, 10), Stack(Potion, 9995), Stackable, 4));
    }

    [Fact]
    public void ASplitIntoAnEmptySlotCopiesTheStackRecord()
    {
        var inv = new Inventory();
        inv.EnsureLength(BagSlot + 1);
        inv[GridSlot] = Stack(Potion, 10, flag: Rented);

        inv.Split(GridSlot, BagSlot, 4);

        Assert.Equal(Stack(Potion, 6, flag: Rented), inv[GridSlot]);
        Assert.Equal(Stack(Potion, 4, flag: Rented), inv[BagSlot]);
    }

    [Fact]
    public void ASplitOntoAStackAddsToIt()
    {
        var inv = new Inventory();
        inv.EnsureLength(BagSlot + 1);
        inv[BagSlot] = Stack(Potion, 10);
        inv[GridSlot] = Stack(Potion, 5);

        inv.Split(BagSlot, GridSlot, 4);

        Assert.Equal(6, inv[BagSlot].Count);
        Assert.Equal(9, inv[GridSlot].Count);
    }
}
