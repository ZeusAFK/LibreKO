using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class NpcHostilityTests
{
    private static EntitySnapshot Summon(int nation) => new()
    {
        IsNpc = true, IsMonster = true, NpcType = NpcTypes.GuardSummon, Nation = nation,
    };

    [Fact]
    public void MyNationsSummonedGuardIsNeverATarget()
    {
        Assert.False(NpcHostility.IsHostile(Summon(Nations.Karus), Nations.Karus, npcsAreTargets: true));
    }

    [Fact]
    public void AnEnemySummonedGuardIsATargetOnlyWhereNpcsAre()
    {
        Assert.True(NpcHostility.IsHostile(Summon(Nations.ElMorad), Nations.Karus, npcsAreTargets: true));
        Assert.False(NpcHostility.IsHostile(Summon(Nations.ElMorad), Nations.Karus, npcsAreTargets: false));
    }

    [Fact]
    public void AnOrdinaryMonsterStaysATarget()
    {
        var mob = new EntitySnapshot { IsNpc = true, IsMonster = true, NpcType = NpcTypes.Monster };
        Assert.True(NpcHostility.IsHostile(mob, Nations.Karus, npcsAreTargets: false));
    }
}
