using Xunit;
using LibreKO;

namespace LibreKO.Tests;

public class BridgeTests
{
    [Fact]
    public void BridgeLoweredPitchConstant_Is110Degrees()
    {
        Assert.Equal(110.0f, World.BridgeLoweredPitch);
    }
}
