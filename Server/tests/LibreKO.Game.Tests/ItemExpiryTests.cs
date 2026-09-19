using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class ItemExpiryTests
{
    private const int ItemId = 379200000;

    private static ItemSlot[] Slots(int count = InventoryConstants.InventoryTotal) =>
        [.. Enumerable.Range(0, count).Select(_ => new ItemSlot())];

    [Fact]
    public void ADayGrantExpiresADayLater()
    {
        var slot = new ItemSlot { ItemId = ItemId, Count = 1 };
        slot.ExpireInDays(1, 1_000);
        slot.ExpiresAt.Should().Be(1_000 + ItemSlot.SecondsPerDay);
        slot.State.Should().Be(ItemFlag.Rented);
        slot.HasExpired(1_000 + ItemSlot.SecondsPerDay - 1).Should().BeFalse();
        slot.HasExpired(1_000 + ItemSlot.SecondsPerDay).Should().BeTrue();
    }

    [Fact]
    public void AGrantWithoutDaysNeverExpires()
    {
        var slot = new ItemSlot { ItemId = ItemId, Count = 1 };
        slot.ExpireInDays(0, 1_000);
        slot.Expires.Should().BeFalse();
        slot.HasExpired(long.MaxValue).Should().BeFalse();
    }

    [Fact]
    public void TheSweepClearsOnlyTheSlotsThatRanOut()
    {
        var slots = Slots(3);
        slots[0].ItemId = ItemId;
        slots[0].ExpiresAt = 500;
        slots[1].ItemId = ItemId;
        slots[1].ExpiresAt = 2_000;
        slots[2].ItemId = ItemId;

        ItemExpiry.Sweep(slots, 1_000).Should().Equal(0);
        slots[0].IsEmpty.Should().BeTrue();
        slots[1].IsEmpty.Should().BeFalse();
        slots[2].IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void TheSweepHonoursItsRange()
    {
        var slots = Slots(4);
        foreach (var slot in slots)
        {
            slot.ItemId = ItemId;
            slot.ExpiresAt = 500;
        }

        ItemExpiry.Sweep(slots, 1_000, start: 1, count: 2).Should().Equal(1, 2);
        slots[0].IsEmpty.Should().BeFalse();
        slots[3].IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void ExpiryOutlivesASaveAndReload()
    {
        var saved = Slots();
        saved[InventoryConstants.InventoryStart].ItemId = ItemId;
        saved[InventoryConstants.InventoryStart].Count = 2;
        saved[InventoryConstants.InventoryStart].ExpireInDays(3, 1_000);

        var loaded = Slots();
        UserSessionBinaryState.LoadSlots(loaded, UserSessionBinaryState.SerializeSlots(saved));

        var slot = loaded[InventoryConstants.InventoryStart];
        slot.ItemId.Should().Be(ItemId);
        slot.Count.Should().Be(2);
        slot.ExpiresAt.Should().Be(1_000 + 3L * ItemSlot.SecondsPerDay);
        slot.State.Should().Be(ItemFlag.Rented);
    }

    [Fact]
    public void ABlobSavedBeforeExpiryExistedStillLoads()
    {
        var saved = Slots();
        saved[InventoryConstants.InventoryStart].ItemId = ItemId;
        saved[InventoryConstants.InventoryStart].Count = 2;
        saved[InventoryConstants.InventoryStart].Flag = (byte)ItemFlag.Bound;

        var full = UserSessionBinaryState.SerializeSlots(saved);
        var withoutExpiry = full[..(saved.Length * (UserSessionBinaryState.BytesPerItem
            + UserSessionBinaryState.BytesPerFlag))];

        var loaded = Slots();
        UserSessionBinaryState.LoadSlots(loaded, withoutExpiry);

        var slot = loaded[InventoryConstants.InventoryStart];
        slot.ItemId.Should().Be(ItemId);
        slot.Count.Should().Be(2);
        slot.State.Should().Be(ItemFlag.Bound);
        slot.Expires.Should().BeFalse();
    }
}
