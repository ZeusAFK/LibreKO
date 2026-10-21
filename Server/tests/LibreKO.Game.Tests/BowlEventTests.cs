using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class BowlEventTests : GameTestBase
{
    private const byte RonarkLandZone = 71;

    [Fact]
    public void IsInBowl_ReturnsExpected_ForVariousCoordinates()
    {
        using var provider = CreateProvider(seed: _ => { });
        var bowlService = provider.GetRequiredService<IBowlEventService>();

        // Center of Bowl (1014, 1007) in zone 71
        bowlService.IsInBowl(RonarkLandZone, 1014.0f, 1007.0f).Should().BeTrue();

        // Edge of radius 131 in zone 71
        bowlService.IsInBowl(RonarkLandZone, 1014.0f + 100.0f, 1007.0f).Should().BeTrue();

        // Far outside Bowl (500, 500)
        bowlService.IsInBowl(RonarkLandZone, 500.0f, 500.0f).Should().BeFalse();

        // Inside coordinates but wrong zone (e.g. Moradon 21)
        bowlService.IsInBowl(21, 1014.0f, 1007.0f).Should().BeFalse();
    }

    [Fact]
    public async Task TickAsync_PlayerInsideBowl_Level60Plus_ReceivesRewards_WhenActive()
    {
        using var provider = CreateProvider(seed: _ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var bowlService = provider.GetRequiredService<IBowlEventService>();

        // Setup player
        var player = CreatePlayer(sessionManager, characterId: 1001, accountId: 2001, out var packets);
        player.Name = "BowlWarrior";
        player.ZoneId = RonarkLandZone;
        player.Level = 60; // User explicitly requested min level 60
        player.Hp = 1000;
        player.X = 1014.0f;
        player.Z = 1007.0f;
        player.KnightCash = 10;
        player.Money = 5000;

        // First tick: player enters bowl
        await bowlService.TickAsync();
        bowlService.ActivePlayersInBowl.Should().Be(1);

        // Player moves to satisfy Anti-AFK (more than 2 meters)
        player.X = 1020.0f;
        player.Z = 1020.0f;

        // Force interval expiry
        bowlService.IntervalSeconds = 0;

        // Second tick: should distribute rewards
        await bowlService.TickAsync();

        // Verify rewards awarded
        player.KnightCash.Should().Be(15); // +5 KC
        player.Money.Should().Be(55000);   // +50,000 Noah

        // Verify client received notice packets
        packets.Any(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHAT).Should().BeTrue();
    }

    [Fact]
    public async Task TickAsync_PlayerBelowLevel60_DoesNotReceiveRewards()
    {
        using var provider = CreateProvider(seed: _ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var bowlService = provider.GetRequiredService<IBowlEventService>();

        var player = CreatePlayer(sessionManager, characterId: 1002, accountId: 2002, out _);
        player.Name = "NoobHero";
        player.ZoneId = RonarkLandZone;
        player.Level = 59; // Below level 60
        player.Hp = 1000;
        player.X = 1014.0f;
        player.Z = 1007.0f;
        player.KnightCash = 0;

        await bowlService.TickAsync();
        bowlService.ActivePlayersInBowl.Should().Be(0);
        player.KnightCash.Should().Be(0);
    }

    [Fact]
    public async Task TickAsync_PlayerAfkWithoutMoving_DoesNotReceiveRewards()
    {
        using var provider = CreateProvider(seed: _ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var bowlService = provider.GetRequiredService<IBowlEventService>();

        var player = CreatePlayer(sessionManager, characterId: 1003, accountId: 2003, out _);
        player.Name = "AfkMiner";
        player.ZoneId = RonarkLandZone;
        player.Level = 75;
        player.Hp = 1000;
        player.X = 1014.0f;
        player.Z = 1007.0f;
        player.KnightCash = 100;

        // First tick: enters bowl
        await bowlService.TickAsync();
        bowlService.ActivePlayersInBowl.Should().Be(1);

        // Interval expired, but player did NOT move at all
        bowlService.IntervalSeconds = 0;

        // Second tick
        await bowlService.TickAsync();

        // KC should remain unchanged because player was AFK
        player.KnightCash.Should().Be(100);

        // Player now moves
        player.X = 1025.0f;
        player.Z = 1025.0f;

        // Third tick after moving
        await bowlService.TickAsync();

        // Now receives rewards!
        player.KnightCash.Should().Be(105);
    }

    [Fact]
    public async Task AdminCommand_BowlToggles_WorkCorrectly()
    {
        using var provider = CreateProvider(seed: _ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var adminCoordinator = provider.GetRequiredService<IAdminPacketCoordinator>();
        var bowlService = provider.GetRequiredService<IBowlEventService>();

        var gm = CreatePlayer(sessionManager, characterId: 9999, accountId: 9999, out _);
        gm.IsGM = true;
        gm.Name = "AdminGM";

        // Toggle off
        await adminCoordinator.HandleGmCommandAsync(gm, "+bowl off");
        bowlService.IsActive.Should().BeFalse();

        // Toggle on (default 60 minutes)
        await adminCoordinator.HandleGmCommandAsync(gm, "+bowl on");
        bowlService.IsActive.Should().BeTrue();
        bowlService.EndTimeServer.Should().NotBeNull();
        bowlService.EndTimeServer!.Value.Should().BeAfter(DateTime.Now.AddMinutes(58));

        // Toggle on with custom minutes
        await adminCoordinator.HandleGmCommandAsync(gm, "+bowl on 30");
        bowlService.IsActive.Should().BeTrue();
        bowlService.EndTimeServer!.Value.Should().BeBefore(DateTime.Now.AddMinutes(31));

        // Set interval
        await adminCoordinator.HandleGmCommandAsync(gm, "+bowl interval 45");
        bowlService.IntervalSeconds.Should().Be(45);
    }

    [Fact]
    public async Task TickAsync_EventAutoExpires_WhenEndTimeElapsed()
    {
        using var provider = CreateProvider(seed: _ => { });
        var bowlService = provider.GetRequiredService<IBowlEventService>();

        bowlService.IsActive = true;
        bowlService.EndTimeServer = DateTime.Now.AddSeconds(-1); // Expired

        await bowlService.TickAsync();

        bowlService.IsActive.Should().BeFalse();
        bowlService.EndTimeServer.Should().BeNull();
    }

    [Fact]
    public async Task BroadcastScreenNotice_SendsScreenNoticeToAllPlayers()
    {
        using var provider = CreateProvider(seed: _ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var bowlService = provider.GetRequiredService<IBowlEventService>();

        var player1 = CreatePlayer(sessionManager, characterId: 1001, accountId: 2001, out var packets1);
        var player2 = CreatePlayer(sessionManager, characterId: 1002, accountId: 2002, out var packets2);

        await bowlService.BroadcastScreenNoticeAsync("[Bowl Event] Test Announcement");

        // Both players must receive GS_NOTICE (screen notice)
        packets1.Any(p => p.GetOpcode() == (byte)GameOpcodes.GS_NOTICE).Should().BeTrue();
        packets2.Any(p => p.GetOpcode() == (byte)GameOpcodes.GS_NOTICE).Should().BeTrue();
    }

    private static UserSession CreatePlayer(
        SessionManager sessionManager, int characterId, int accountId, out List<Packet> sentPackets, int money = 0)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var captured = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        sentPackets = captured;

        var session = sessionManager.CreateSession(client, characterId, accountId);
        session.ZoneId = RonarkLandZone;
        session.Level = 80;
        session.MaxHp = 5000;
        session.Hp = 5000;
        session.Money = money;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }
}
