using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class RepairPriceTests
{
    [Fact]
    public void AnUndamagedItemCostsNothing()
    {
        Assert.Equal(0, RepairPrice.Cost(20000, 8000, 8000));
        Assert.Equal(0, RepairPrice.Cost(20000, 0, 0));
    }

    [Fact]
    public void TheCostGrowsWithTheMissingDurability()
    {
        int half = RepairPrice.Cost(20000, 8000, 4000);
        int full = RepairPrice.Cost(20000, 8000, 0);
        Assert.True(half > 0);
        Assert.InRange(full, half * 2 - 1, half * 2 + 1);
    }
}
