using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class FrameLimitTests
{
    [Theory]
    [InlineData(60, true, 60.0, 0)]
    [InlineData(144, true, 60.0, 0)]
    [InlineData(60, true, 59.94, 0)]
    [InlineData(30, true, 60.0, 30)]
    [InlineData(60, false, 60.0, 60)]
    [InlineData(120, true, 144.0, 120)]
    [InlineData(60, true, -1.0, 60)]
    [InlineData(0, false, 60.0, 0)]
    public void VsyncPacesAnyCapAtOrAboveTheRefreshRate(int cap, bool vsync, double refresh, int expected)
    {
        Assert.Equal(expected, FrameLimit.EngineCap(cap, vsync, refresh));
    }
}
