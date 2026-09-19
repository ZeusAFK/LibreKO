using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class NpcSpawnTests
{
    private const int BurnsLog = 30200;
    private const byte NpcPathActType = 105;
    private const short OreadsZone = 66;
    private const string TwoWaypoints = "0100020003000400";

    private static NpcData Proto() => new()
    {
        Id = BurnsLog,
        IsMonster = false,
        Name = "Burns Log",
        NpcType = 11,
        Hp = 100,
        Level = 1,
    };

    private static NpcPosData Pos(
        byte actType = 1, short regTime = 0, short regenType = 0,
        byte dotCnt = 0, string? path = null, short spawnRange = 0) => new()
    {
        Index = 1,
        ZoneId = OreadsZone,
        NpcId = BurnsLog,
        ActType = actType,
        RegTime = regTime,
        RegenType = regenType,
        DotCnt = dotCnt,
        Path = path,
        LeftX = 500,
        TopZ = 600,
        NumNPC = 1,
        SpawnRange = spawnRange,
    };

    [Fact]
    public void TheRespawnDelayComesFromTheSpawnRow()
    {
        var npc = NpcInstance.FromData(Proto(), Pos(regTime: 3600), 1);

        npc.RespawnDelayMs.Should().Be(3_600_000, "a row asking for an hour must not come back in thirty seconds");
    }

    [Fact]
    public void ASpawnRowWithNoRespawnTimeKeepsTheDefault()
    {
        var npc = NpcInstance.FromData(Proto(), Pos(regTime: 0), 1);

        npc.RespawnDelayMs.Should().Be(NpcInstance.DefaultRespawnDelayMs);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void OnlyTheNeverRespawnTypeStaysDead(short regenType, bool expected)
    {
        var npc = NpcInstance.FromData(Proto(), Pos(regenType: regenType), 1);

        npc.CanRespawn.Should().Be(expected,
            "the one-life flag is recorded but nothing acts on it yet");
    }

    [Fact]
    public void AnUnknownRespawnTypeIsTreatedAsNormal()
    {
        var npc = NpcInstance.FromData(Proto(), Pos(regenType: 9), 1);

        npc.RespawnType.Should().Be(NpcRespawnType.Normal);
        npc.CanRespawn.Should().BeTrue();
    }

    [Fact]
    public void APathFollowingNpcKeepsItsMoveType()
    {
        var npc = NpcInstance.FromData(
            Proto(), Pos(actType: NpcPathActType, dotCnt: 2, path: TwoWaypoints), 1);

        npc.MoveType.Should().Be(NpcMoveType.ScriptedPath,
            "an authored path must not be flattened to standing still");
        npc.Waypoints.Should().HaveCount(2);
    }

    [Fact]
    public void APathFollowingNpcWithNoWaypointsWanders()
    {
        var npc = NpcInstance.FromData(Proto(), Pos(actType: NpcPathActType), 1);

        npc.MoveType.Should().Be(NpcMoveType.Wander);
    }

    [Fact]
    public void AnOutOfRangeMoveTypeStandsStill()
    {
        var npc = NpcInstance.FromData(Proto(), Pos(actType: 200), 1);

        npc.MoveType.Should().Be(NpcMoveType.None);
    }

    [Fact]
    public void AFixedSpawnRowPutsTheNpcExactlyOnItsPoint()
    {
        var npc = NpcInstance.FromData(Proto(), Pos(spawnRange: 0), 1);

        npc.SpawnX.Should().Be(500);
        npc.SpawnZ.Should().Be(600);
    }

    [Fact]
    public void ASpawnRangeScattersWithinItsRadiusOnBothAxes()
    {
        for (var i = 0; i < 200; i++)
        {
            var npc = NpcInstance.FromData(Proto(), Pos(spawnRange: 7), 1);

            npc.SpawnX.Should().BeInRange(493, 507);
            npc.SpawnZ.Should().BeInRange(593, 607);
        }
    }
}
