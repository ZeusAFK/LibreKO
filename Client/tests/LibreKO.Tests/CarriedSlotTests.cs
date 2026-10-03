using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class CarriedSlotTests
{
    [Theory]
    [InlineData(InventoryConstants.RightHand, false)]
    [InlineData(InventoryConstants.InventoryStart, true)]
    [InlineData(InventoryConstants.CospreStart - 1, true)]
    [InlineData(InventoryConstants.CospreStart, false)]
    [InlineData(InventoryConstants.BagSlotStart, false)]
    [InlineData(InventoryConstants.MagicBagStart, true)]
    public void OnlyBagAndMagicBagItemsAreCarried(int abs, bool carried) =>
        Assert.Equal(carried, ItemMove.IsCarried(abs));
}
