using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public sealed class BagStackTransferTests
{
    private const int Bag = InventoryConstants.MagicBagStart;
    private const int Grid = InventoryConstants.InventoryStart;
    private const int Potion = 389010000;
    private const int OtherItem = 389011000;
    private const int Stackable = 1;
    private const int Single = 0;
    private const int NoTarget = -1;
    private const int BagCount = 100;
    private const int GridCount = 200;

    private static ItemSlot Stack(int itemId, int count, byte flag = 0, int uniqueId = 0) =>
        new() { ItemId = itemId, Count = (short)count, Flag = flag, UniqueId = uniqueId };

    private static Inventory Seed()
    {
        var inventory = new Inventory();
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        inventory[Bag] = Stack(Potion, BagCount);
        inventory[Grid + 1] = Stack(Potion, GridCount);
        return inventory;
    }

    private static void FillGrid(Inventory inventory, int keep)
    {
        for (int abs = Grid; abs < Grid + Inventory.GridCount; abs++)
            if (abs != keep) inventory[abs] = Stack(OtherItem, 1);
    }

    [Fact]
    public void BlankDropTargetFillsTheExistingStack()
    {
        var inventory = Seed();
        Assert.Equal(Grid + 1, inventory.PlanBagToGrid(Bag, Grid, Stackable));
    }

    [Fact]
    public void RightClickFillsTheExistingStack()
    {
        var inventory = Seed();
        Assert.Equal(Grid + 1, inventory.PlanBagToGrid(Bag, NoTarget, Stackable));
        Assert.Equal(Grid + 1, inventory.FirstStackOrFreeGridSlot(inventory[Bag], Stackable));
    }

    [Fact]
    public void ADroppedOnStackIsKept()
    {
        var inventory = Seed();
        inventory[Grid + 2] = Stack(Potion, GridCount);
        Assert.Equal(Grid + 2, inventory.PlanBagToGrid(Bag, Grid + 2, Stackable));
    }

    [Fact]
    public void AStackWithoutRoomForTheWholeStackFallsBackToAFreeSlot()
    {
        var inventory = Seed();
        inventory[Grid + 1] = Stack(Potion, Inventory.StackMax - BagCount + 1);
        Assert.Equal(Grid + 2, inventory.PlanBagToGrid(Bag, Grid + 2, Stackable));
        Assert.Equal(Grid, inventory.PlanBagToGrid(Bag, NoTarget, Stackable));
    }

    [Fact]
    public void AStackFilledExactlyToTheMaximumStillMerges()
    {
        var inventory = Seed();
        inventory[Grid + 1] = Stack(Potion, Inventory.StackMax - BagCount);
        Assert.Equal(Grid + 1, inventory.PlanBagToGrid(Bag, Grid, Stackable));
    }

    [Fact]
    public void AFullInventoryStillAllowsAStackMerge()
    {
        var inventory = Seed();
        FillGrid(inventory, Grid + 1);
        Assert.Equal(Grid + 1, inventory.PlanBagToGrid(Bag, NoTarget, Stackable));
    }

    [Fact]
    public void AFullInventoryWithoutAMatchingStackHasNoTarget()
    {
        var inventory = Seed();
        FillGrid(inventory, NoTarget);
        Assert.Equal(NoTarget, inventory.PlanBagToGrid(Bag, NoTarget, Stackable));
    }

    [Theory]
    [InlineData((byte)ItemFlag.Sealed, 0)]
    [InlineData((byte)0, 7)]
    public void DifferentFlagsAndLinkedItemsKeepTheirOwnSlot(byte flag, int uniqueId)
    {
        var inventory = Seed();
        inventory[Grid + 1] = Stack(Potion, GridCount, flag, uniqueId);
        Assert.Equal(Grid, inventory.PlanBagToGrid(Bag, Grid, Stackable));
    }

    [Fact]
    public void ItemsThatDoNotStackKeepTheDropTarget()
    {
        var inventory = Seed();
        Assert.Equal(Grid, inventory.PlanBagToGrid(Bag, Grid, Single));
    }

    [Fact]
    public void OnlyAMagicBagSourceIsPlanned()
    {
        var inventory = Seed();
        Assert.Equal(NoTarget, inventory.PlanBagToGrid(Grid + 1, Grid, Stackable));
        Assert.Equal(NoTarget, inventory.PlanBagToGrid(Bag + 1, Grid, Stackable));
    }
}
