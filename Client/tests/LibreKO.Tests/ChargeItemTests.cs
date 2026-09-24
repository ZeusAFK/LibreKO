using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ChargeItemTests
{
    private static readonly ItemData.Item SpeedUpPack = new() { Kind = 255, Countable = 0, Duration = 30, Weight = 10 };
    private static readonly ItemData.Item Voucher = new() { Kind = 255, Countable = 1, Duration = 1, Weight = 5 };
    private static readonly ItemData.Item Arrow = new() { Kind = 0, Countable = 1, Duration = 1, Weight = 1 };

    [Fact]
    public void AChargeItemShowsItsUsesNotItsStack()
    {
        var pack = new ItemSlot { ItemId = 1, Count = 1, Durability = 30 };
        Assert.Equal(30, ItemData.ShownCount(SpeedUpPack, pack));
        Assert.Equal(1, ItemData.CarriedUnits(SpeedUpPack, pack));
    }

    [Fact]
    public void ACountableItemShowsAndWeighsItsStack()
    {
        var arrows = new ItemSlot { ItemId = 2, Count = 500, Durability = 1 };
        Assert.Equal(500, ItemData.ShownCount(Arrow, arrows));
        Assert.Equal(500, ItemData.CarriedUnits(Arrow, arrows));

        var vouchers = new ItemSlot { ItemId = 3, Count = 4, Durability = 1 };
        Assert.Equal(4, ItemData.ShownCount(Voucher, vouchers));
    }

    [Fact]
    public void AnOldSaveWithTheUsesInTheStackStillWeighsOneItem()
    {
        var pack = new ItemSlot { ItemId = 1, Count = 29, Durability = 29 };
        Assert.Equal(1, ItemData.CarriedUnits(SpeedUpPack, pack));
    }
}
