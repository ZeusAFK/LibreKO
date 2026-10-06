using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class TransformationUseTests
{
    [Theory]
    [InlineData(TransformationUse.Npc)]
    [InlineData(TransformationUse.NpcEvent)]
    public void HumanoidLooksKeepTheAccessories(int use) =>
        Assert.True(TransformationUse.WearsAccessories(use));

    [Theory]
    [InlineData(TransformationUse.Siege)]
    [InlineData(TransformationUse.Monster)]
    [InlineData(TransformationUse.Costume)]
    [InlineData(TransformationUse.GuardTower)]
    [InlineData(TransformationUse.MovingTower)]
    [InlineData(2)]
    public void OtherLooksHideThem(int use) =>
        Assert.False(TransformationUse.WearsAccessories(use));
}
