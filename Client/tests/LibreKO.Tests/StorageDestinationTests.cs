using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class StorageDestinationTests
{
    private const int Expiry = 1_900_000_000;
    private const int PetSerial = 4321;

    private static ItemSlot Item(int id) => new() { ItemId = id };

    [Fact]
    public void ExistingStackIsPreferredOverAnEarlierEmptySlot()
    {
        ItemSlot[] slots = [default, new() { ItemId = 10, Count = 20 }];
        Assert.Equal(1, StorageDestination.Find(slots, Item(10), 100, true));
    }

    [Fact]
    public void FullStacksAreSkippedAndEquipmentDoesNotMerge()
    {
        ItemSlot[] slots = [new() { ItemId = 10, Count = Inventory.StackMax }, default];
        Assert.Equal(1, StorageDestination.Find(slots, Item(10), 1, true));
        slots[0].Count = 1;
        Assert.Equal(1, StorageDestination.Find(slots, Item(10), 1, false));
    }

    [Fact]
    public void InsufficientRoomDoesNotSelectAnOccupiedSlot()
    {
        ItemSlot[] slots = [new() { ItemId = 10, Count = Inventory.StackMax - 5 }, new() { ItemId = 20, Count = 1 }];
        Assert.Equal(-1, StorageDestination.Find(slots, Item(10), 6, true));
        Assert.Equal(0, StorageDestination.Find(slots, Item(10), 5, true));
        Assert.False(StorageDestination.Fits(slots[0], Item(10), 0, true));
    }

    [Fact]
    public void FullStorageHasNoDestination()
    {
        ItemSlot[] slots = [new() { ItemId = 20, Count = 1 }, new() { ItemId = 30, Count = 1 }];
        Assert.Equal(-1, StorageDestination.Find(slots, Item(10), 1, true));
        Assert.Equal(-1, StorageDestination.Find(slots, Item(10), 1, false));
    }

    [Fact]
    public void AStackOfAnotherBindingOrExpiryIsNotJoined()
    {
        ItemSlot[] slots = [new() { ItemId = 10, Count = 5, Flag = (byte)ItemFlag.Bound }, new() { ItemId = 10, Count = 5, ExpiresAt = Expiry }, default];
        Assert.Equal(2, StorageDestination.Find(slots, Item(10), 1, true));
        Assert.Equal(0, StorageDestination.Find(slots, new ItemSlot { ItemId = 10, Flag = (byte)ItemFlag.Bound }, 1, true));
        Assert.Equal(1, StorageDestination.Find(slots, new ItemSlot { ItemId = 10, ExpiresAt = Expiry }, 1, true));
    }

    [Fact]
    public void ALinkedItemNeverJoinsAStack()
    {
        ItemSlot[] slots = [new() { ItemId = 10, Count = 1 }, default];
        Assert.Equal(1, StorageDestination.Find(slots, new ItemSlot { ItemId = 10, UniqueId = PetSerial }, 1, true));
    }
}
