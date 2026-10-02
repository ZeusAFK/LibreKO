using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class FxBlendMathTests
{
    private static readonly FxBlendMath.Rgb Ground = new(0.40f, 0.30f, 0.20f);
    private static readonly FxBlendMath.Rgb Spark = new(0.90f, 0.60f, 0.10f);

    private static FxBlendMath.Rgb Direct(int src, int dest, FxBlendMath.Rgb s, float a, FxBlendMath.Rgb d)
    {
        float keep = FxBlendMath.DestinationKeep(dest, s, a);
        return new(
            s.R * FxBlendMath.SourceWeight(src, s.R, a) + d.R * keep,
            s.G * FxBlendMath.SourceWeight(src, s.G, a) + d.G * keep,
            s.B * FxBlendMath.SourceWeight(src, s.B, a) + d.B * keep);
    }

    private static void Near(FxBlendMath.Rgb expected, FxBlendMath.Rgb actual)
    {
        Assert.Equal(expected.R, actual.R, 4);
        Assert.Equal(expected.G, actual.G, 4);
        Assert.Equal(expected.B, actual.B, 4);
    }

    [Theory]
    [InlineData(FxBlendMath.SrcAlpha, FxBlendMath.One)]
    [InlineData(FxBlendMath.SrcAlpha, FxBlendMath.InvSrcAlpha)]
    [InlineData(FxBlendMath.One, FxBlendMath.One)]
    [InlineData(FxBlendMath.Zero, FxBlendMath.InvSrcAlpha)]
    [InlineData(FxBlendMath.SrcColor, FxBlendMath.One)]
    [InlineData(FxBlendMath.One, FxBlendMath.InvSrcAlpha)]
    [InlineData(FxBlendMath.Zero, FxBlendMath.SrcAlpha)]
    [InlineData(FxBlendMath.One, FxBlendMath.Zero)]
    public void APremultipliedDrawMatchesTheFixedFunctionBlend(int src, int dest)
    {
        var fx = FxBlendMath.Shade(src, dest, true, Spark, 0.6f, 1);
        Near(Direct(src, dest, Spark, 0.6f, Ground), FxBlendMath.Over(fx, Ground));
    }

    [Theory]
    [InlineData(FxBlendMath.SrcAlpha, FxBlendMath.One, 3)]
    [InlineData(FxBlendMath.SrcAlpha, FxBlendMath.InvSrcAlpha, 4)]
    [InlineData(FxBlendMath.Zero, FxBlendMath.InvSrcAlpha, 2)]
    public void LayersEqualDrawingTheSameQuadThatManyTimes(int src, int dest, int layers)
    {
        var once = FxBlendMath.Shade(src, dest, true, Spark, 0.45f, 1);
        var expected = Ground;
        for (int i = 0; i < layers; i++) expected = FxBlendMath.Over(once, expected);

        var stacked = FxBlendMath.Shade(src, dest, true, Spark, 0.45f, layers);
        Near(expected, FxBlendMath.Over(stacked, Ground));
    }

    [Fact]
    public void AnUnblendedPartReplacesTheBackground()
    {
        var fx = FxBlendMath.Shade(FxBlendMath.SrcAlpha, FxBlendMath.InvSrcAlpha, false, Spark, 0.2f, 3);
        Near(Spark, FxBlendMath.Over(fx, Ground));
    }

    [Theory]
    [InlineData(FxBlendMath.SrcAlpha, FxBlendMath.One, true, 1)]
    [InlineData(FxBlendMath.SrcColor, FxBlendMath.InvSrcColor, false, 4)]
    [InlineData(FxBlendMath.Zero, FxBlendMath.InvSrcAlpha, true, FxBlendMath.MaxLayers)]
    public void PackingRoundTrips(int src, int dest, bool blended, int layers)
    {
        Assert.Equal((src, dest, blended, layers), FxBlendMath.Unpack(FxBlendMath.Pack(src, dest, blended, layers)));
    }

    [Fact]
    public void EveryPackedWordSurvivesHalfPrecision()
    {
        for (int src = FxBlendMath.Zero; src <= FxBlendMath.InvSrcAlpha; src++)
        for (int dest = FxBlendMath.Zero; dest <= FxBlendMath.InvSrcAlpha; dest++)
        for (int layers = 1; layers <= FxBlendMath.MaxLayers; layers++)
        foreach (bool blended in new[] { true, false })
        {
            int packed = FxBlendMath.Pack(src, dest, blended, layers);
            Assert.InRange(packed, 0, FxBlendMath.HalfFloatExactLimit - 1);
            Assert.Equal(packed, (int)(float)(System.Half)packed);
        }
    }
}
