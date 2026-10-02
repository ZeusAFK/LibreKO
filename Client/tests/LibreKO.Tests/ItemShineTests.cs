using Xunit;

namespace LibreKO.Tests;

public class ItemShineTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    [InlineData(10, 4)]
    [InlineData(30, 4)]
    public void UpgradeGlowStartsAtPlusSeven(int plus, int level) =>
        Assert.Equal(level, ItemShine.LevelForPlus(plus));
}
