using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Tests;

public class FriendTeleportTests
{
    [Fact]
    public void AFriendInMoradonCanBeReached()
    {
        FriendTeleport.ZoneAllows((byte)ZoneId.KarusCamp1, AccountNation.Karus, (byte)ZoneId.Moradon, reborn: false)
            .Should().BeTrue();
    }

    [Fact]
    public void TheEnemyNationsHomeZoneIsRefused()
    {
        FriendTeleport.ZoneAllows((byte)ZoneId.Moradon, AccountNation.Karus, (byte)ZoneId.ElMoradCamp1, reborn: false)
            .Should().BeFalse();
        FriendTeleport.ZoneAllows((byte)ZoneId.Moradon, AccountNation.Karus, (byte)ZoneId.KarusCamp1, reborn: false)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData((byte)ZoneId.ChaosDungeon)]
    [InlineData((byte)ZoneId.BorderDefenseWar)]
    [InlineData((byte)ZoneId.JuradMountain)]
    public void EventZonesAreRefusedBothWays(byte eventZone)
    {
        FriendTeleport.ZoneAllows((byte)ZoneId.Moradon, AccountNation.Karus, eventZone, reborn: false).Should().BeFalse();
        FriendTeleport.ZoneAllows(eventZone, AccountNation.Karus, (byte)ZoneId.Moradon, reborn: false).Should().BeFalse();
    }

    [Fact]
    public void ARebornCharacterCannotJoinAFriendInArdream()
    {
        FriendTeleport.ZoneAllows((byte)ZoneId.Moradon, AccountNation.Karus, (byte)ZoneId.Ardream, reborn: true)
            .Should().BeFalse();
    }
}
