using System.Runtime.CompilerServices;
using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Game.Tests;

public class FullMoonRiftMapTests : GameTestBase
{
    private const byte Rift = (byte)ZoneId.DungeonDefence;
    private const int RiftMapPoints = 65;
    private const float ArrivedDistance = 3f;
    private const int MaxSteps = 600;

    private static SmdFile RiftMap([CallerFilePath] string source = "") =>
        SmdFile.Load(Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Map", "dungeon_defense.smd")))!;

    [Fact]
    public void TheRiftHasItsOwnMap()
    {
        var map = RiftMap();

        map.Should().NotBeNull();
        map.MapSize.Should().Be(RiftMapPoints);
    }

    [Fact]
    public void PlayersMonstersTowersAndPriestsStandOnWalkableGround()
    {
        var map = RiftMap();

        map.IsWalkableAtPosition(FullMoonRules.SpawnX, FullMoonRules.SpawnZ).Should().BeTrue();
        map.IsWalkableAtPosition(FullMoonRules.MonsterSpawnX, FullMoonRules.MonsterSpawnZ).Should().BeTrue();
        foreach (var (_, x, z) in FullMoonRules.InitialTowers.Concat(FullMoonRules.InitialPriests))
            map.IsWalkableAtPosition(x, z).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MonstersWalkFromTheirSpawnToEveryTower(int tower)
    {
        var (_, towerX, towerZ) = FullMoonRules.InitialTowers[tower];
        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var movementService = provider.GetRequiredService<INpcAiMovementService>();
        var map = RiftMap();
        sessionManager.Maps = CreateMapManagerWithMap(Rift, map);
        var monster = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true,
            ZoneId = Rift,
            X = FullMoonRules.MonsterSpawnX,
            Z = FullMoonRules.MonsterSpawnZ,
            SpawnX = FullMoonRules.MonsterSpawnX,
            SpawnZ = FullMoonRules.MonsterSpawnZ,
            Speed2 = 7,
            State = NpcState.Attacking,
            Hp = 100,
            MaxHp = 100,
            NpcType = 0
        });

        var steps = 0;
        while (MathF.Sqrt((towerX - monster.X) * (towerX - monster.X) + (towerZ - monster.Z) * (towerZ - monster.Z)) > ArrivedDistance
               && steps++ < MaxSteps)
        {
            await movementService.MoveTowardPointAsync(monster, towerX, towerZ, DateTime.UtcNow.Ticks);
            map.IsWalkableAtPosition(monster.X, monster.Z).Should().BeTrue();
        }

        steps.Should().BeLessThan(MaxSteps);
    }
}
