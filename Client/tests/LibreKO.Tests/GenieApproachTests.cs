using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class GenieApproachTests
{
    [Fact]
    public void RAttackClosesInWhileTheAttackSkillWaitsInReach()
    {
        Assert.True(GenieApproach.ShouldMove(false, true, false, true, true));
    }

    [Fact]
    public void RAttackStaysPutOnceTheMonsterIsInBasicReach()
    {
        Assert.False(GenieApproach.ShouldMove(false, true, true, true, true));
        Assert.False(GenieApproach.ShouldMove(false, true, true, false, false));
    }

    [Fact]
    public void RAttackAloneWalksToAMonsterOutOfReach()
    {
        Assert.True(GenieApproach.ShouldMove(false, true, false, false, false));
    }

    [Fact]
    public void SkillsAloneStopAtTheSkillsReach()
    {
        Assert.True(GenieApproach.ShouldMove(false, false, false, true, false));
        Assert.False(GenieApproach.ShouldMove(false, false, false, true, true));
    }

    [Fact]
    public void NothingToAttackWithMeansNoApproach()
    {
        Assert.False(GenieApproach.ShouldMove(false, false, false, false, false));
    }

    [Fact]
    public void HuntingInPlaceNeverWalks()
    {
        Assert.False(GenieApproach.ShouldMove(true, true, false, true, false));
        Assert.False(GenieApproach.ShouldMove(true, false, false, true, false));
    }
}
