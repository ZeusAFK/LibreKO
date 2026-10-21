using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class BowlEventTests : GameTestBase
{
    private const byte RonarkLandZone = 71;
    private const float BowlCenterX = 1014f;
    private const float BowlCenterZ = 1007f;
    private const int ScheduledHour = 14;
    private const int LaterHour = 19;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ServiceProvider Provider(Clock clock, Action<GameServerSettings>? configureSettings = null) =>
        CreateProvider(
            seed: _ => { },
            configureSettings: settings =>
            {
                settings.BowlEvent.StartHours = [];
                configureSettings?.Invoke(settings);
            },
            configureServices: services => services.AddSingleton<TimeProvider>(clock));

    private static void Scheduled(GameServerSettings settings) => settings.BowlEvent.StartHours = [ScheduledHour, LaterHour];

    private static TimeSpan Interval(IBowlEventService bowl) => TimeSpan.FromSeconds(bowl.IntervalSeconds);

    [Fact]
    public void IsInBowl_CoversTheCraterInRonarkLandOnly()
    {
        using var provider = Provider(new Clock());
        var bowl = provider.GetRequiredService<IBowlEventService>();

        bowl.IsInBowl(RonarkLandZone, BowlCenterX, BowlCenterZ).Should().BeTrue();
        bowl.IsInBowl(RonarkLandZone, BowlCenterX + 100f, BowlCenterZ).Should().BeTrue();
        bowl.IsInBowl(RonarkLandZone, BowlCenterX + 140f, BowlCenterZ).Should().BeFalse();
        bowl.IsInBowl(RonarkLandZone, 500f, 500f).Should().BeFalse();
        bowl.IsInBowl(21, BowlCenterX, BowlCenterZ).Should().BeFalse();
    }

    [Fact]
    public async Task TickAsync_NothingRunsUntilAGameMasterStartsIt()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var player = CreatePlayer(provider, characterId: 1001, accountId: 2001, out _);
        player.KnightCash = 10;

        await bowl.TickAsync();
        player.X += 10f;
        clock.Now += Interval(bowl);
        await bowl.TickAsync();

        bowl.IsActive.Should().BeFalse();
        bowl.PlayersInBowl.Should().Be(0);
        player.KnightCash.Should().Be(10);
    }

    [Fact]
    public async Task TickAsync_PlayerWhoMovesIsPaidEachInterval()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var player = CreatePlayer(provider, characterId: 1001, accountId: 2001, out var packets);
        player.Level = 60;
        player.KnightCash = 10;
        player.Money = 5000;

        await bowl.StartAsync();
        await bowl.TickAsync();
        bowl.PlayersInBowl.Should().Be(1);

        player.X += 6f;
        player.Z += 13f;
        clock.Now += Interval(bowl);
        await bowl.TickAsync();

        player.KnightCash.Should().Be(15);
        player.Money.Should().Be(55000);
        packets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHAT);
    }

    [Fact]
    public async Task TickAsync_PlayerBelowTheMinimumLevelIsNotCounted()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var player = CreatePlayer(provider, characterId: 1002, accountId: 2002, out _);
        player.Level = 59;
        player.KnightCash = 0;

        await bowl.StartAsync();
        await bowl.TickAsync();

        bowl.PlayersInBowl.Should().Be(0);
        player.KnightCash.Should().Be(0);
    }

    [Fact]
    public async Task TickAsync_IdlePlayerIsPaidOnlyAfterMoving()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var player = CreatePlayer(provider, characterId: 1003, accountId: 2003, out _);
        player.KnightCash = 100;

        await bowl.StartAsync();
        await bowl.TickAsync();
        clock.Now += Interval(bowl);
        await bowl.TickAsync();
        player.KnightCash.Should().Be(100);

        player.X += 11f;
        player.Z += 18f;
        await bowl.TickAsync();
        player.KnightCash.Should().Be(105);
    }

    [Fact]
    public async Task TickAsync_CoinsStopAtThePurseCap()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var player = CreatePlayer(provider, characterId: 1004, accountId: 2004, out _);
        player.Money = ExchangePacketConstants.CoinMax - 10;

        await bowl.StartAsync();
        await bowl.TickAsync();
        player.X += 10f;
        clock.Now += Interval(bowl);
        await bowl.TickAsync();

        player.Money.Should().Be(ExchangePacketConstants.CoinMax);
    }

    [Fact]
    public async Task TickAsync_PlayerWhoLeftTheGameIsDropped()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var player = CreatePlayer(provider, characterId: 1005, accountId: 2005, out _);

        await bowl.StartAsync();
        await bowl.TickAsync();
        bowl.PlayersInBowl.Should().Be(1);

        sessionManager.RemoveSession(player);
        await bowl.TickAsync();

        bowl.PlayersInBowl.Should().Be(0);
    }

    [Fact]
    public async Task TickAsync_EventEndsWhenItsTimeIsUp()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var bowl = provider.GetRequiredService<IBowlEventService>();
        CreatePlayer(provider, characterId: 1006, accountId: 2006, out var packets);

        await bowl.StartAsync(durationMinutes: 1);
        await bowl.TickAsync();
        packets.Clear();
        clock.Now += TimeSpan.FromSeconds(61);
        await bowl.TickAsync();

        bowl.IsActive.Should().BeFalse();
        bowl.PlayersInBowl.Should().Be(0);
        packets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_NOTICE);
    }

    [Fact]
    public async Task TickScheduleAsync_StartsOnceAtEachListedHour()
    {
        using var provider = Provider(new Clock(), Scheduled);
        var bowl = (BowlEventService)provider.GetRequiredService<IBowlEventService>();
        var opening = new DateTime(2026, 10, 21, ScheduledHour, 0, 0);

        (await bowl.TickScheduleAsync(opening.AddMinutes(-1))).Should().BeFalse();
        (await bowl.TickScheduleAsync(opening)).Should().BeTrue();
        bowl.IsActive.Should().BeTrue();

        await bowl.StopAsync();
        (await bowl.TickScheduleAsync(opening.AddSeconds(30))).Should().BeFalse();
        bowl.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task TickScheduleAsync_LeavesTheEventOffWhenItIsTurnedOff()
    {
        using var provider = Provider(new Clock(), settings =>
        {
            Scheduled(settings);
            settings.BowlEvent.Enabled = false;
        });
        var bowl = (BowlEventService)provider.GetRequiredService<IBowlEventService>();

        await bowl.TickScheduleAsync(new DateTime(2026, 10, 21, ScheduledHour, 0, 0));

        bowl.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task EventsWindow_ListsTheBowlTimesAndTheRunningEvent()
    {
        using var provider = Provider(new Clock(), Scheduled);
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var window = ActivatorUtilities.CreateInstance<EventSchedulePacketCoordinator>(provider);

        window.Events(DateTime.UtcNow).Should()
            .ContainSingle(e => e.Kind == EventScheduleKind.Bowl).Which.Slots.Should().HaveCount(2);
        window.Now().Should().NotContain(e => e.Kind == EventScheduleKind.Bowl);

        await bowl.StartAsync();

        window.Now().Should().ContainSingle(e => e.Kind == EventScheduleKind.Bowl);
    }

    [Fact]
    public async Task StartAsync_AnnouncesToEveryPlayer()
    {
        using var provider = Provider(new Clock());
        var bowl = provider.GetRequiredService<IBowlEventService>();
        CreatePlayer(provider, characterId: 1001, accountId: 2001, out var first);
        CreatePlayer(provider, characterId: 1002, accountId: 2002, out var second);

        await bowl.StartAsync();

        first.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_NOTICE);
        second.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_NOTICE);
    }

    [Fact]
    public async Task GmCommand_StartsStopsAndSetsTheInterval()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var admin = provider.GetRequiredService<IAdminPacketCoordinator>();
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var gm = CreatePlayer(provider, characterId: 9999, accountId: 9999, out _);
        gm.IsGM = true;

        await admin.HandleGmCommandAsync(gm, "+bowl on");
        bowl.IsActive.Should().BeTrue();
        bowl.Remaining.Should().Be(TimeSpan.FromMinutes(bowl.Settings.DefaultDurationMinutes));

        await admin.HandleGmCommandAsync(gm, "+bowl on 30");
        bowl.Remaining.Should().Be(TimeSpan.FromMinutes(30));

        await admin.HandleGmCommandAsync(gm, "+bowl interval 45");
        bowl.IntervalSeconds.Should().Be(45);

        await admin.HandleGmCommandAsync(gm, "+bowl off");
        bowl.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GmCommand_CannotStartTheEventWhenItIsTurnedOff()
    {
        using var provider = Provider(new Clock(), settings => settings.BowlEvent.Enabled = false);
        var admin = provider.GetRequiredService<IAdminPacketCoordinator>();
        var bowl = provider.GetRequiredService<IBowlEventService>();
        var gm = CreatePlayer(provider, characterId: 9999, accountId: 9999, out _);
        gm.IsGM = true;

        await admin.HandleGmCommandAsync(gm, "+bowl on");

        bowl.IsActive.Should().BeFalse();
    }

    private static UserSession CreatePlayer(ServiceProvider provider, int characterId, int accountId, out List<Packet> sentPackets)
    {
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var captured = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(captured.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        sentPackets = captured;

        var session = sessionManager.CreateSession(client, characterId, accountId);
        session.Name = $"Bowl{characterId}";
        session.ZoneId = RonarkLandZone;
        session.Level = 80;
        session.MaxHp = 5000;
        session.Hp = 5000;
        session.X = BowlCenterX;
        session.Z = BowlCenterZ;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }
}
