using Godot;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class UnitBlockingTests
{
    private static readonly Vector2 Unit = Vector2.Zero;

    [Theory]
    [InlineData(NpcTypes.Monster)]
    [InlineData(NpcTypes.Boss)]
    public void YouWalkThroughMonsters(int npcType) =>
        Assert.False(UnitBlocking.CanBlock(attackable: true, isMonster: true, npcType));

    [Fact]
    public void AnEnemyGuardSummonStillBlocks() =>
        Assert.True(UnitBlocking.CanBlock(attackable: true, isMonster: true, NpcTypes.GuardSummon));

    [Theory]
    [InlineData(NpcTypes.Scarecrow)]
    [InlineData(NpcTypes.Guard)]
    public void AnAttackableNpcThatIsNotAMonsterStillBlocks(int npcType) =>
        Assert.True(UnitBlocking.CanBlock(attackable: true, isMonster: false, npcType));

    [Fact]
    public void OnlyAnAttackablePlayerBlocks()
    {
        Assert.True(UnitBlocking.CanBlock(attackable: true, isMonster: false, npcType: default));
        Assert.False(UnitBlocking.CanBlock(attackable: false, isMonster: false, npcType: default));
    }

    [Fact]
    public void AUnitThatIsNotATargetNeverBlocks() =>
        Assert.False(UnitBlocking.CanBlock(attackable: false, isMonster: true, NpcTypes.GuardSummon));

    [Fact]
    public void AStepIntoAUnitsSpaceIsBlocked()
    {
        Assert.True(UnitBlocking.Blocks(new Vector2(2f, 0f), new Vector2(1.3f, 0f), Unit, 1.8f, 1.8f));
    }

    [Fact]
    public void SteppingAwayOrPastTheSpaceIsAllowed()
    {
        Assert.False(UnitBlocking.Blocks(new Vector2(1.3f, 0f), new Vector2(1.4f, 0f), Unit, 1.8f, 1.8f));
        Assert.False(UnitBlocking.Blocks(new Vector2(3f, 0f), new Vector2(2f, 0f), Unit, 1.8f, 1.8f));
    }

    [Fact]
    public void TallerUnitsKeepYouFurtherAway()
    {
        Assert.True(UnitBlocking.Blocks(new Vector2(4f, 0f), new Vector2(3.5f, 0f), Unit, 1.8f, 8f));
        Assert.False(UnitBlocking.Blocks(new Vector2(4f, 0f), new Vector2(3.5f, 0f), Unit, 1.8f, 1.8f));
    }
}
