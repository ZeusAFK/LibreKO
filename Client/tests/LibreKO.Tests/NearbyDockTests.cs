using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class NearbyDockTests
{
    [Theory]
    [InlineData(150f, 6)]
    [InlineData(151f, 6)]
    [InlineData(174f, 7)]
    [InlineData(10f, 1)]
    public void TheListShowsTheRowsThatFit(float height, int rows) =>
        Assert.Equal(rows, NearbyDock.RowsThatFit(height, rowHeight: 24f, separation: 1f));

    [Theory]
    [InlineData(5, 20, 6, 5)]
    [InlineData(18, 20, 6, 14)]
    [InlineData(-3, 20, 6, 0)]
    [InlineData(4, 3, 6, 0)]
    public void TheFirstRowStaysInRange(int first, int count, int visible, int expected) =>
        Assert.Equal(expected, NearbyDock.ClampFirst(first, count, visible));
}
