using System;
using Xunit;
using LibreKO;

namespace LibreKO.Tests;

public class BridgeTests
{
    [Theory]
    [InlineData(224f, 645f, 1, 0f, 224f, 18.88f, 645f)]        // Trap 1
    [InlineData(309f, 848f, 2, -90f, 309f, 18.88f, 848f)]      // Trap 2
    [InlineData(512f, 767f, 3, 180f, 512f, 18.88f, 767f)]      // Trap 3
    [InlineData(800f, 375f, 4, 180f, 800f, 18.88f, 374.8f)]    // Trap 4
    [InlineData(715f, 172f, 5, 90f, 714.8f, 18.88f, 172f)]     // Trap 5
    [InlineData(512f, 257f, 6, 0f, 512f, 18.88f, 257f)]        // Trap 6
    public void VerifyBridgeTrapsAndYaws(float x, float z, int expectedTrap, float expectedYaw, float expectedHingeX, float expectedHingeY, float expectedHingeZ)
    {
        int trap = World.TrapNumberForPosition(x, z);
        float yaw = World.BridgeYawForPosition(x, z);
        var hinge = World.BridgeHingeForTrap(trap);
        Assert.Equal(expectedTrap, trap);
        Assert.Equal(expectedYaw, yaw);
        Assert.Equal(expectedYaw, World.BridgeYawForTrap(trap));
        Assert.Equal(expectedHingeX, hinge.X);
        Assert.Equal(expectedHingeY, hinge.Y);
        Assert.Equal(expectedHingeZ, hinge.Z);
    }
}
