using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class MasteryPointsTests
{
    private const int Blade = 205;
    private const int Protector = 206;
    private const int Beginner = 201;

    private const int Attack = MasteryPoints.FirstTree;
    private const int Master = MasteryPoints.MasterTree;

    private static MasteryPoints With(int pool, int attack = 0, int master = 0)
    {
        var m = new MasteryPoints();
        var slots = new byte[MasteryPoints.SlotCount];
        slots[MasteryPoints.PoolSlot] = (byte)pool;
        slots[Attack] = (byte)attack;
        slots[Master] = (byte)master;
        m.Seed(slots);
        return m;
    }

    [Fact]
    public void CanSpend_NeedsAPoolPoint()
    {
        Assert.False(With(pool: 0).CanSpend(Blade, Attack, level: 60));
        Assert.True(With(pool: 1).CanSpend(Blade, Attack, level: 60));
    }

    [Theory]
    [InlineData(19, 20, true)]
    [InlineData(20, 20, false)]
    [InlineData(20, 21, true)]
    public void CanSpend_CapsATreeAtTheCharacterLevel(int spent, int level, bool allowed)
    {
        Assert.Equal(allowed, With(pool: 5, attack: spent).CanSpend(Blade, Attack, level));
    }

    [Fact]
    public void CanSpend_RefusesEveryTreeForABeginnerClass()
    {
        var m = With(pool: 10);
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
            Assert.False(m.CanSpend(Beginner, tree, level: 60));
    }

    [Fact]
    public void CanSpend_ReservesTheMasterTreeForMasterClasses()
    {
        Assert.False(With(pool: 10).CanSpend(Blade, Master, level: 70));
        Assert.True(With(pool: 10).CanSpend(Protector, Master, level: 70));
    }

    [Theory]
    [InlineData(60, 0)]
    [InlineData(70, 10)]
    [InlineData(83, MasteryPoints.MasterTreeMaxPoints)]
    [InlineData(90, MasteryPoints.MasterTreeMaxPoints)]
    public void CapInTree_GrowsTheMasterTreeOnePerLevelAboveSixty(int level, int expected)
    {
        Assert.Equal(expected, MasteryPoints.CapInTree(Protector, Master, level));
    }

    [Fact]
    public void CanSpend_ReopensWhenTheCharacterLevelsUp()
    {
        var m = With(pool: 4, attack: 20);
        Assert.False(m.CanSpend(Blade, Attack, level: 20));
        Assert.True(m.CanSpend(Blade, Attack, level: 21));
    }

    [Fact]
    public void Spend_MovesOnePointOutOfThePool()
    {
        var m = With(pool: 2, attack: 7);
        Assert.True(m.Spend(Attack));
        Assert.Equal(1, m.Pool);
        Assert.Equal(8, m.InTree(Attack));
    }

    [Fact]
    public void Spend_IsRefusedWithAnEmptyPool_SoAnOptimisticApplyCannotGoNegative()
    {
        var m = With(pool: 0, attack: 7);
        Assert.False(m.Spend(Attack));
        Assert.Equal(0, m.Pool);
        Assert.Equal(7, m.InTree(Attack));
    }

    [Fact]
    public void ApplyRejection_RestoresTheServerValueAndGivesThePointBack()
    {
        var m = With(pool: 3, attack: 7);
        m.Spend(Attack);
        m.ApplyRejection(Attack, serverValue: 7);
        Assert.Equal(3, m.Pool);
        Assert.Equal(7, m.InTree(Attack));
    }

    [Fact]
    public void ResetTrees_ClearsEveryTreeAndSeatsTheRefundedPool()
    {
        var m = With(pool: 1, attack: 20, master: 5);
        m.ResetTrees(pool: 42);
        Assert.Equal(42, m.Pool);
        for (int tree = MasteryPoints.FirstTree; tree <= MasteryPoints.LastTree; tree++)
            Assert.Equal(0, m.InTree(tree));
    }

    [Fact]
    public void Seed_IgnoresASlotArrayShorterThanTheWireLayout()
    {
        var m = new MasteryPoints();
        m.Seed(new byte[] { 9, 1 });
        Assert.Equal(9, m.Pool);
        Assert.Equal(0, m.InTree(Attack));
    }
}
