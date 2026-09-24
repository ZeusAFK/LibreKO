using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SkillFlightTests
{
    [Theory]
    [InlineData(MagicType.DotHeal, true, true, true)]
    [InlineData(MagicType.Buff, true, true, true)]
    [InlineData(MagicType.Ranged, true, true, true)]
    [InlineData(MagicType.Ranged, false, true, true)]
    [InlineData(MagicType.DotHeal, false, true, false)]
    [InlineData(MagicType.DotHeal, true, false, false)]
    [InlineData(MagicType.Melee, true, true, false)]
    public void ASkillWithAFlyingEffectLaunchesItBeforeItLands(int type1, bool hasFlyingFx, bool hasCastPhase, bool expected)
    {
        Assert.Equal(expected, SkillData.FlyingStageFor(type1, hasFlyingFx, hasCastPhase));
    }

    [Fact]
    public void AProjectileInTheAirIsAlreadyReleased()
    {
        Assert.True(CastInterrupt.IsReleased(now: 11.0, castEndsAt: 10.5, stagePending: true));
    }

    [Fact]
    public void ACastStillOnItsBarIsNotReleased()
    {
        Assert.False(CastInterrupt.IsReleased(now: 10.0, castEndsAt: 10.5, stagePending: true));
    }

    [Fact]
    public void ACastWhoseStageWasSentIsReleased()
    {
        Assert.True(CastInterrupt.IsReleased(now: 10.0, castEndsAt: 10.5, stagePending: false));
    }
}
