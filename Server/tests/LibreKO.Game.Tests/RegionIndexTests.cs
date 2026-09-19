using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class RegionIndexTests
{
    private const byte Moradon = 21;
    private const byte ElMoradFrontier = 1;
    private const float RegionSpan = RegionManager.RegionSize;

    [Fact]
    public void RemoveFromRegion_DropsTheEntry_AfterZoneAndPositionAlreadyMoved()
    {
        var regions = new RegionManager();
        var traveller = CreateSession(characterId: 24, Moradon, x: 810f, z: 530f);
        regions.AddToRegion(traveller);

        traveller.ZoneId = ElMoradFrontier;
        traveller.X = 100f;
        traveller.Z = 100f;

        regions.RemoveFromRegion(traveller);
        regions.AddToRegion(traveller);

        var viewer = CreateSession(characterId: 25, Moradon, x: 810f, z: 530f);
        regions.AddToRegion(viewer);

        regions.GetNearbyUsers(viewer).Should().BeEmpty();
    }

    [Fact]
    public void GetNearbyUsers_NeverYieldsTheSameCharacterTwice_AfterAZoneRoundTrip()
    {
        var regions = new RegionManager();
        var traveller = CreateSession(characterId: 24, Moradon, x: 810f, z: 530f);
        regions.AddToRegion(traveller);

        traveller.ZoneId = ElMoradFrontier;
        traveller.X = 100f;
        traveller.Z = 100f;
        regions.RemoveFromRegion(traveller);
        regions.AddToRegion(traveller);

        traveller.ZoneId = Moradon;
        traveller.X = 810f + RegionSpan;
        traveller.Z = 530f;
        regions.RemoveFromRegion(traveller);
        regions.AddToRegion(traveller);

        var viewer = CreateSession(characterId: 25, Moradon, x: 810f, z: 530f);
        regions.AddToRegion(viewer);

        var nearby = regions.GetNearbyUsers(viewer).ToList();

        nearby.Select(u => u.CharacterId).Should().OnlyHaveUniqueItems();
        nearby.Should().ContainSingle().Which.CharacterId.Should().Be(24);
    }

    [Fact]
    public void RemoveFromRegion_OfAStaleSession_LeavesTheTakeoverSessionRegistered()
    {
        var regions = new RegionManager();
        var stale = CreateSession(characterId: 24, Moradon, x: 810f, z: 530f);
        regions.AddToRegion(stale);

        var takeover = CreateSession(characterId: 24, Moradon, x: 810f, z: 530f);
        regions.AddToRegion(takeover);

        regions.RemoveFromRegion(stale);

        var viewer = CreateSession(characterId: 25, Moradon, x: 810f, z: 530f);
        regions.AddToRegion(viewer);

        regions.GetNearbyUsers(viewer).Should().ContainSingle().Which.Should().BeSameAs(takeover);
    }

    private static UserSession CreateSession(int characterId, byte zoneId, float x, float z)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var session = new UserSession(client, characterId, accountId: characterId + 1000)
        {
            Name = $"Char{characterId}",
            ZoneId = zoneId,
            X = x,
            Z = z,
        };
        return session;
    }
}
