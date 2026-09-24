using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BlinkPathTests
{
    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(0f, 1f)]
    [InlineData(-0.6f, -0.8f)]
    public void TheBlinkGoesTheWayTheCharacterFaces(float koDx, float koDz)
    {
        float rotationY = 180f - Coord.KoHeading(koDx, koDz);
        var (x, z) = BlinkPath.KoDirection(rotationY);
        Assert.Equal(koDx, x, 3);
        Assert.Equal(koDz, z, 3);
    }

    [Fact]
    public void TheDestinationTravelsInTenthsOfAMetre()
    {
        var data = BlinkPath.Data(118.04f, 7.5f, 106.26f);
        Assert.Equal(1180, data[0]);
        Assert.Equal(75, data[1]);
        Assert.Equal(1063, data[2]);
    }

    [Fact]
    public void ACoordinatePastAShortKeepsItsLowSixteenBits()
    {
        var data = BlinkPath.Data(3315f, 0f, 100f);
        Assert.Equal(33150, (ushort)data[0]);
    }
}
