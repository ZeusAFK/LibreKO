using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SlotHoldTests
{
    private static readonly ItemSlot Before = new() { ItemId = 156210008, Count = 1, Durability = 7000 };
    private static readonly ItemSlot After = new() { ItemId = 156210009, Count = 1, Durability = 15000 };

    [Fact]
    public void AHeldSlotShowsWhatItHeldUntilReleased()
    {
        var hold = new SlotHold();
        hold.Hold(20, Before);
        Assert.Equal(Before.ItemId, hold.Shown(20, After).ItemId);
        hold.Release();
        Assert.False(hold.Active);
        Assert.Equal(After.ItemId, hold.Shown(20, After).ItemId);
    }

    [Fact]
    public void OtherSlotsShowTheLiveItem()
    {
        var hold = new SlotHold();
        hold.Hold(20, Before);
        Assert.Equal(After.ItemId, hold.Shown(21, After).ItemId);
    }
}
