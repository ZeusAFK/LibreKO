using FluentAssertions;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class MapWalkabilityTests : GameTestBase
{
    private const short Zone = 21;
    private const int MapSize = 8;
    private const short Walkable = 1;
    private const short EventRegion = 36;

    [Fact]
    public void GroundAndEventRegionsAreWalkableWhileBlockedTilesAndTheOutsideAreNot()
    {
        var maps = CreateMapManagerWithTiles(Zone, MapSize, unitDistance: 1, defaultEventId: Walkable);
        var tiles = maps.GetMap(Zone)!.EventTiles;
        tiles[2 * MapSize + 3] = SmdFile.BlockedTile;
        tiles[4 * MapSize + 4] = EventRegion;

        maps.IsMovable(Zone, 1.5f, 1.5f).Should().BeTrue();
        maps.IsMovable(Zone, 2.5f, 3.5f).Should().BeFalse();
        maps.IsMovable(Zone, 4.5f, 4.5f).Should().BeTrue();
        maps.IsMovable(Zone, -1f, 1.5f).Should().BeFalse();
        maps.IsMovable(Zone, MapSize + 1f, 1.5f).Should().BeFalse();
    }

    [Fact]
    public void OpenGroundNeedsNoDetour()
    {
        var map = CreateMapManagerWithTiles(Zone, MapSize, unitDistance: 1, defaultEventId: Walkable).GetMap(Zone)!;

        NpcPathfinder.FindNextStep(map, 0.5f, 0.5f, 6.5f, 0.5f).Should().BeNull();
    }

    [Fact]
    public void AnNpcGoesAroundAWall()
    {
        var map = CreateMapManagerWithTiles(Zone, MapSize, unitDistance: 1, defaultEventId: Walkable).GetMap(Zone)!;
        for (var z = 0; z < MapSize - 1; z++)
            map.EventTiles[3 * MapSize + z] = SmdFile.BlockedTile;

        var step = NpcPathfinder.FindNextStep(map, 0.5f, 0.5f, 6.5f, 0.5f);

        step.Should().NotBeNull();
        map.IsWalkableAtPosition(step!.Value.x, step.Value.z).Should().BeTrue();
        step.Value.z.Should().BeGreaterThan(0.5f);
    }
}
