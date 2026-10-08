using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class StorageDestinationTests
{
    [Fact]
    public void ExistingStackIsPreferredOverAnEarlierEmptySlot()
    {
        ItemSlot[] slots = [default, new() { ItemId = 10, Count = 20 }];
        Assert.Equal(1, StorageDestination.Find(slots, 10, 100, true));
    }

    [Fact]
    public void FullStacksAreSkippedAndEquipmentDoesNotMerge()
    {
        ItemSlot[] slots = [new() { ItemId = 10, Count = Inventory.StackMax }, default];
        Assert.Equal(1, StorageDestination.Find(slots, 10, 1, true));
        slots[0].Count = 1;
        Assert.Equal(1, StorageDestination.Find(slots, 10, 1, false));
    }

    [Fact]
    public void InsufficientRoomDoesNotSelectAnOccupiedSlot()
    {
        ItemSlot[] slots = [new() { ItemId = 10, Count = Inventory.StackMax - 5 }, new() { ItemId = 20, Count = 1 }];
        Assert.Equal(-1, StorageDestination.Find(slots, 10, 6, true));
        Assert.Equal(0, StorageDestination.Find(slots, 10, 5, true));
        Assert.False(StorageDestination.Fits(slots[0], 10, 0, true));
    }

    [Fact]
    public void FullStorageHasNoDestination()
    {
        ItemSlot[] slots = [new() { ItemId = 20, Count = 1 }, new() { ItemId = 30, Count = 1 }];
        Assert.Equal(-1, StorageDestination.Find(slots, 10, 1, true));
        Assert.Equal(-1, StorageDestination.Find(slots, 10, 1, false));
    }
}
