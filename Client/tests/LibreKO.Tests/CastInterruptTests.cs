using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class CastInterruptTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MovingAfterTheReleaseNeverCancelsTheSkill(bool rangedDraw, bool pastRangedCommit)
    {
        Assert.Equal(CastInterruptAction.Ignore,
            CastInterrupt.OnMove(hasCastPhase: true, released: true, rangedDraw, pastRangedCommit));
    }

    [Fact]
    public void MovingDuringASpellCastCancelsIt()
    {
        Assert.Equal(CastInterruptAction.Cancel,
            CastInterrupt.OnMove(hasCastPhase: true, released: false, rangedDraw: false, pastRangedCommit: true));
    }

    [Theory]
    [InlineData(false, CastInterruptAction.Cancel)]
    [InlineData(true, CastInterruptAction.ReleaseEarly)]
    public void MovingDuringABowDrawCancelsItBeforeTheCommitPointAndLoosesItAfter(
        bool pastRangedCommit, CastInterruptAction expected)
    {
        Assert.Equal(expected,
            CastInterrupt.OnMove(hasCastPhase: true, released: false, rangedDraw: true, pastRangedCommit));
    }

    [Fact]
    public void AnInstantSkillIsNeverCancelledByMovement()
    {
        Assert.Equal(CastInterruptAction.Ignore,
            CastInterrupt.OnMove(hasCastPhase: false, released: false, rangedDraw: false, pastRangedCommit: false));
    }
}
