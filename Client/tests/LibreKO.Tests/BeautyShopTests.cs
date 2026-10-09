using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BeautyShopTests
{
    private const int OtherItem = 389010000;
    private const int BagSlot = Inventory.GridStart + 5;
    private const int EquipSlot = InventoryConstants.Breast;

    private static Inventory Bag()
    {
        var inventory = new Inventory();
        inventory.EnsureLength(InventoryConstants.InventoryTotal);
        return inventory;
    }

    private static ItemSlot Item(int itemId) => new() { ItemId = itemId, Count = 1 };

    [Fact]
    public void ACouponInTheBagPaysForTheChange()
    {
        var inventory = Bag();
        inventory[BagSlot] = Item(BeautyShop.Coupon);

        Assert.True(BeautyShop.HasCoupon(inventory));
    }

    [Fact]
    public void ACouponInAMagicBagPaysForTheChange()
    {
        var inventory = Bag();
        inventory[InventoryConstants.MagicBagStart] = Item(BeautyShop.Coupon);

        Assert.True(BeautyShop.HasCoupon(inventory));
    }

    [Fact]
    public void WithoutACouponTheChangeIsNotSent()
    {
        var inventory = Bag();
        inventory[BagSlot] = Item(OtherItem);

        Assert.False(BeautyShop.HasCoupon(inventory));
        Assert.False(BeautyShop.HasCoupon(new Inventory()));
    }

    [Fact]
    public void ACouponOutsideTheBagDoesNotCount()
    {
        var inventory = Bag();
        inventory[EquipSlot] = Item(BeautyShop.Coupon);

        Assert.False(BeautyShop.HasCoupon(inventory));
    }

    [Theory]
    [InlineData(true, BeautyShop.SucceededText)]
    [InlineData(false, BeautyShop.FailedText)]
    public void TheResultUsesTheRetailText(bool succeeded, int text) =>
        Assert.Equal(text, BeautyShop.ResultText(succeeded));
}
