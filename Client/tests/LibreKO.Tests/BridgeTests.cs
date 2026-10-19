using Xunit;
using LibreKO;
using LibreKO.Features.Core;

namespace LibreKO.Tests;

public class BridgeTests
{
    [Fact]
    public void TheLoweredBridgeTiltsMinus110DegreesOnTheMirroredModel()
    {
        Assert.Equal(-110.0f, EntitySystem.BridgeLoweredPitch);
    }
}
