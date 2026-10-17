using Xunit;

namespace LibreKO.Tests;

public class FxLodTests
{
    [Fact]
    public void AThirdOfTheEmissionsSurviveOverTime()
    {
        float carry = 0f;
        int kept = 0;
        for (int frame = 0; frame < 30; frame++)
            kept += FxLod.Thin(1, 1f / 3f, ref carry, keepOne: false);
        Assert.InRange(kept, 9, 10);
    }

    [Fact]
    public void ABurstAlwaysKeepsOneParticle()
    {
        float carry = 0f;
        Assert.Equal(1, FxLod.Thin(1, 0.1f, ref carry, keepOne: true));
    }

    [Fact]
    public void FullRatioLeavesEmissionAlone()
    {
        float carry = 0f;
        Assert.Equal(7, FxLod.Thin(7, 1f, ref carry, keepOne: false));
    }

    [Theory]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    public void ACapAtOrBelowTheDefaultBecomesTheTarget(int frameCap, int expected)
    {
        Assert.Equal(expected, FxLod.TargetFps(frameCap));
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(120, 60)]
    [InlineData(138, 60)]
    [InlineData(144, 60)]
    [InlineData(240, 60)]
    public void NoCapOrAHigherCapKeepsTheDefaultTarget(int frameCap, int expected)
    {
        Assert.Equal(expected, FxLod.TargetFps(frameCap));
    }
}
