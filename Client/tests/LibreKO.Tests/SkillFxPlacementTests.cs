using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SkillFxPlacementTests
{
    private const int GroundPart = 255;
    private const int GroundSentinelPart = 254;
    private const int BonePart = 0;
    private const int Caster = 710;
    private const int Victim = 12045;

    [Theory]
    [InlineData(GroundPart, Victim)]
    [InlineData(GroundPart, Caster)]
    [InlineData(GroundSentinelPart, Victim)]
    public void AnAreaCastDrawsNoGroundBurstUnderTheEntitiesItHits(int part, int targetId)
    {
        Assert.Equal(ImpactFxPlacement.None, SkillFxTarget.Placement(part, targetId, areaCast: true));
    }

    [Theory]
    [InlineData(GroundPart)]
    [InlineData(GroundSentinelPart)]
    public void AnAreaCastDrawsItsGroundBurstOnceAtTheImpactPoint(int part)
    {
        Assert.Equal(ImpactFxPlacement.AtImpactPoint,
            SkillFxTarget.Placement(part, SkillFxTarget.AreaImpactTarget, areaCast: true));
    }

    [Fact]
    public void ASingleTargetGroundEffectLandsUnderItsTarget()
    {
        Assert.Equal(ImpactFxPlacement.UnderEntity, SkillFxTarget.Placement(GroundPart, Victim, areaCast: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABoneEffectFollowsEveryEntityItHitsAndNeverTheImpactPoint(bool areaCast)
    {
        Assert.Equal(ImpactFxPlacement.OnEntity, SkillFxTarget.Placement(BonePart, Victim, areaCast));
        Assert.Equal(ImpactFxPlacement.None,
            SkillFxTarget.Placement(BonePart, SkillFxTarget.AreaImpactTarget, areaCast));
    }
}
