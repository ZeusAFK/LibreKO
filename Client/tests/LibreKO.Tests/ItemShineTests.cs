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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(20, 3)]
    [InlineData(21, 4)]
    [InlineData(30, 4)]
    public void ReverseGlowFollowsTheReverseLadder(int plus, int level) =>
        Assert.Equal(level, ItemShine.LevelForReversePlus(plus));

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(0.25f, 0.75f)]
    [InlineData(1f, 0f)]
    [InlineData(1.4f, 0f)]
    [InlineData(-0.2f, 1f)]
    public void TheShineLightFadesOutUnderTheSun(float daylight, float share) =>
        Assert.Equal(share, ItemShine.LightShare(daylight), 3);
}
