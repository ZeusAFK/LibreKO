using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SkillFxPhaseTests
{
    [Fact]
    public void AStopThatNamesNoPhaseReachesTheGenieAura() =>
        Assert.Contains(SkillFxPhase.Aura, SkillFxPhase.All);

    [Theory]
    [InlineData(MagicSub.Casting)]
    [InlineData(MagicSub.Flying)]
    [InlineData(MagicSub.Effecting)]
    public void AStopThatNamesNoPhaseReachesEveryCastStage(int phase) =>
        Assert.Contains(phase, SkillFxPhase.All);
}
