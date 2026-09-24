using System;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class VolleyTests
{
    private const float Step = MathF.PI / 12f;

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(5, 5)]
    public void AVolleyFansOneCentreArrowAndPairsAtFifteenDegrees(int arrows, int expectedCount)
    {
        var offsets = Volley.Offsets(arrows);
        Assert.Equal(expectedCount, offsets.Length);
        Assert.Equal(0f, offsets[0]);
        for (int i = 1; 2 * i < offsets.Length; i++)
        {
            Assert.Equal(-i * Step, offsets[2 * i - 1], 5);
            Assert.Equal(i * Step, offsets[2 * i], 5);
        }
    }

    [Fact]
    public void AStraightArrowHitsTheFirstCreatureOnItsPath()
    {
        var hit = Volley.FirstAlong(0, 0, 20, 0, new[]
        {
            new VolleyCandidate(7, 15, 0.5f, 1f),
            new VolleyCandidate(3, 8, -0.4f, 1f),
            new VolleyCandidate(9, 10, 5f, 1f),
        });
        Assert.Equal(3, hit);
    }

    [Fact]
    public void AStraightArrowThatMeetsNothingHitsNothing()
    {
        Assert.Equal(-1, Volley.FirstAlong(0, 0, 20, 0, new[] { new VolleyCandidate(9, 10, 5f, 1f) }));
    }

    [Theory]
    [InlineData(MagicType.Ranged, 0, false)]
    [InlineData(MagicType.Ranged, 2, true)]
    [InlineData(MagicType.DotHeal, 0, true)]
    public void OnlyGuidedArrowsHomeWhileSpellsAlwaysDo(int type1, int hitType, bool homes)
    {
        Assert.Equal(homes, SkillData.FlightHomesFor(type1, hitType));
    }
}
