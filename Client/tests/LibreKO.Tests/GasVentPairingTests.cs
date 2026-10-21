using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class GasVentPairingTests
{
    [Fact]
    public void EachSprayGoesToOneVentEvenWhenTwoVentsShareTheirNearest()
    {
        var vents = new List<(float X, float Z)> { (0f, 0f), (4f, 0f) };
        var sprays = new List<(float X, float Z)> { (1f, 0f), (9f, 0f) };

        var pairs = GasVentPairing.Pair(vents, sprays, maxDistance: 16f);

        Assert.Equal(new[] { (0, 0), (1, 1) }, pairs.OrderBy(p => p.Vent).ToArray());
    }

    [Fact]
    public void ASprayTooFarFromEveryVentStaysUnpaired()
    {
        var pairs = GasVentPairing.Pair(new List<(float X, float Z)> { (0f, 0f) }, new List<(float X, float Z)> { (40f, 0f) }, maxDistance: 16f);

        Assert.Empty(pairs);
    }
}
