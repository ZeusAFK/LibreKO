using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class InventorySnapshotTests
{
    private const int Sword = 700011001;
    private const int Shield = 700012001;
    private const int SummonedFamiliar = 3;
    private const int ResummonedFamiliar = 4;
    private const int Potion = 389010000;
    private const int Stackable = 1;
    private const int Single = 0;
    private const int Grid = InventoryConstants.InventoryStart;
    private const int Cospre = InventoryConstants.CospreStart;
    private const int BagSlot = InventoryConstants.BagSlotStart;
    private const int MagicBag = InventoryConstants.MagicBagStart;
    private const byte LastGrid = InventoryConstants.HaveMax - 1;
    private const byte LastEquip = InventoryConstants.SlotMax - 1;
    private const byte LastCospre = InventoryConstants.CospreMax - 1;
    private const byte LastBagSlot = InventoryConstants.BagSlotMax - 1;
    private const byte LastMagicBag = InventoryConstants.MagicBagTotal - 1;

    [Theory]
    [InlineData(ItemMove.InventoryToSlot, LastGrid, LastEquip, Grid + LastGrid, LastEquip)]
    [InlineData(ItemMove.SlotToInventory, LastEquip, LastGrid, LastEquip, Grid + LastGrid)]
    [InlineData(ItemMove.InventoryToInventory, 0, LastGrid, Grid, Grid + LastGrid)]
    [InlineData(ItemMove.SlotToSlot, 0, LastEquip, 0, LastEquip)]
    [InlineData(ItemMove.InventoryToCospre, LastGrid, LastCospre, Grid + LastGrid, Cospre + LastCospre)]
    [InlineData(ItemMove.CospreToInventory, LastCospre, LastGrid, Cospre + LastCospre, Grid + LastGrid)]
    [InlineData(ItemMove.InventoryToBagSlot, LastGrid, LastBagSlot, Grid + LastGrid, BagSlot + LastBagSlot)]
    [InlineData(ItemMove.BagSlotToInventory, LastBagSlot, LastGrid, BagSlot + LastBagSlot, Grid + LastGrid)]
    [InlineData(ItemMove.InventoryToMagicBag, LastGrid, LastMagicBag, Grid + LastGrid, MagicBag + LastMagicBag)]
    [InlineData(ItemMove.MagicBagToInventory, LastMagicBag, LastGrid, MagicBag + LastMagicBag, Grid + LastGrid)]
    [InlineData(ItemMove.MagicBagToMagicBag, 0, LastMagicBag, MagicBag, MagicBag + LastMagicBag)]
    public void EveryConfirmedMoveLandsTheRecordOnItsResolvedSlot(byte direction, byte source, byte destination, int expectedFrom, int expectedTo)
    {
        Assert.True(ItemMove.TryResolveSlots(direction, source, destination, out int from, out int to));
        Assert.Equal(expectedFrom, from);
        Assert.Equal(expectedTo, to);
        var cached = new ItemSlot[InventoryConstants.InventoryTotal];
        var item = new ItemSlot { ItemId = Sword, Count = 1, Durability = 100, Flag = 2, UniqueId = 42 };
        cached[from] = item;
        ItemMove.ApplyConfirmed(cached, direction, from, to, Single);
        var newWorld = new Inventory();
        newWorld.Reset(cached);
        Assert.True(newWorld[from].IsEmpty);
        Assert.Equal(item, newWorld[to]);
    }

    [Fact]
    public void ASwapKeepsBothRecordsIntact()
    {
        var cached = new ItemSlot[InventoryConstants.InventoryTotal];
        var held = new ItemSlot { ItemId = Sword, Count = 1, Durability = 80, Flag = 1, UniqueId = 7 };
        var worn = new ItemSlot { ItemId = Shield, Count = 1, Durability = 90, UniqueId = 8 };
        cached[Grid] = held;
        cached[InventoryConstants.RightHand] = worn;
        ItemMove.ApplyConfirmed(cached, ItemMove.InventoryToSlot, Grid, InventoryConstants.RightHand, Single);
        Assert.Equal(worn, cached[Grid]);
        Assert.Equal(held, cached[InventoryConstants.RightHand]);
    }

    [Theory]
    [InlineData(ItemMove.InventoryToInventory, 0, 1)]
    [InlineData(ItemMove.InventoryToMagicBag, 0, LastMagicBag)]
    [InlineData(ItemMove.MagicBagToInventory, LastMagicBag, 0)]
    [InlineData(ItemMove.MagicBagToMagicBag, 0, LastMagicBag)]
    public void ConfirmedStackMergesAddTheCountIntoTheDestination(byte direction, byte source, byte destination)
    {
        Assert.True(ItemMove.TryResolveSlots(direction, source, destination, out int from, out int to));
        var cached = new ItemSlot[InventoryConstants.InventoryTotal];
        cached[from] = new ItemSlot { ItemId = Potion, Count = 3 };
        cached[to] = new ItemSlot { ItemId = Potion, Count = 7 };
        ItemMove.ApplyConfirmed(cached, direction, from, to, Stackable);
        var newWorld = new Inventory();
        newWorld.Reset(cached);
        Assert.True(newWorld[from].IsEmpty);
        Assert.Equal(10, newWorld[to].Count);
    }

    [Theory]
    [InlineData(ItemMove.InventoryToCospre, InventoryConstants.HaveMax, 0)]
    [InlineData(ItemMove.InventoryToCospre, 0, InventoryConstants.CospreMax)]
    [InlineData(ItemMove.InventoryToBagSlot, 0, InventoryConstants.BagSlotMax)]
    [InlineData(ItemMove.MagicBagToInventory, InventoryConstants.MagicBagTotal, 0)]
    [InlineData(ItemMove.InventoryToPet, 0, 0)]
    [InlineData(ItemMove.InventoryToZone, 0, 0)]
    public void InvalidAndExternalSlotsDoNotModifyTheZoneSnapshot(byte direction, byte source, byte destination)
    {
        Assert.False(ItemMove.TryResolveSlots(direction, source, destination, out int from, out int to));
        Assert.Equal(-1, from);
        Assert.Equal(-1, to);
    }

    [Fact]
    public void AFamiliarItemAcknowledgementOnlyReachesTheFamiliarThatWasAsked()
    {
        var item = new ItemSlot { ItemId = Sword, Count = 1, Durability = 50, UniqueId = 9 };
        var asked = new PetSheet { Index = SummonedFamiliar };
        var resummoned = new PetSheet { Index = ResummonedFamiliar };
        Assert.True(asked.PlaceConfirmed(SummonedFamiliar, PetSheet.InventorySize - 1, item));
        Assert.Equal(item, asked.Items[PetSheet.InventorySize - 1]);
        Assert.False(resummoned.PlaceConfirmed(SummonedFamiliar, 0, item));
        Assert.True(resummoned.Items[0].IsEmpty);
        Assert.False(asked.PlaceConfirmed(SummonedFamiliar, PetSheet.InventorySize, item));
    }
}
