using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class MaxDurabilityTests
{
    private static readonly ItemData.Item Raptor = new() { Duration = 8000 };

    [Fact]
    public void UpgradedGearCountsItsDurabilityBonus() =>
        Assert.Equal(15000, ItemData.MaxDurability(Raptor, new ItemData.Ext { DurationBonus = 7000 }));

    [Fact]
    public void AnItemWithoutExtraDataUsesItsBaseDurability()
    {
        Assert.Equal(8000, ItemData.MaxDurability(Raptor, null));
        Assert.Equal(0, ItemData.MaxDurability(null, null));
    }
}
