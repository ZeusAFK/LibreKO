using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class EventSchedulerTests
{
    private readonly SessionManager sessionManager;
    private readonly IOptions<GameServerSettings> settings;
    private readonly IZoneTransitionService zoneTransition;
    private readonly ICollectionRaceService collectionRace;
    private readonly ILotteryService lottery;
    private readonly IJuraidMountainService juraidMountain;
    private readonly IBorderDefenseWarService bdw;
    private readonly IUnderTheCastleService underTheCastle;
    private readonly IForgottenTempleService forgottenTemple;
    private readonly IGameDataService gameData;
    private readonly ILogger<EventSchedulerService> logger;
    private readonly EventSchedulerService scheduler;

    public EventSchedulerTests()
    {
        sessionManager = new SessionManager();
        settings = Options.Create(new GameServerSettings());
        zoneTransition = Substitute.For<IZoneTransitionService>();
        collectionRace = Substitute.For<ICollectionRaceService>();
        lottery = Substitute.For<ILotteryService>();
        juraidMountain = Substitute.For<IJuraidMountainService>();
        bdw = Substitute.For<IBorderDefenseWarService>();
        underTheCastle = Substitute.For<IUnderTheCastleService>();
        forgottenTemple = Substitute.For<IForgottenTempleService>();
        gameData = Substitute.For<IGameDataService>();
        logger = Substitute.For<ILogger<EventSchedulerService>>();

        scheduler = new EventSchedulerService(
            sessionManager,
            settings,
            zoneTransition,
            collectionRace,
            lottery,
            juraidMountain,
            bdw,
            Substitute.For<IChaosDungeonService>(),
            underTheCastle,
            forgottenTemple,
            Substitute.For<IKrowazService>(),
            Substitute.For<INereidMonumentService>(),
            gameData,
            new ScheduleClock(),
            logger);
    }

    private (UserSession session, List<Packet> sentPackets) CreateTestSession(int charId, int level)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var packets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => packets.Add(p)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId: charId, accountId: charId * 10);
        session.Name = $"Player_{charId}";
        session.Nation = AccountNation.ElMorad;
        session.Level = (byte)level;
        session.ZoneId = 21;
        return (session, packets);
    }

    [Fact]
    public async Task CallTempleEventAsync_OnlySendsPopupToEligibleLevelPlayers()
    {
        var (lowPlayer, lowPackets) = CreateTestSession(101, 50);
        var (highPlayer, highPackets) = CreateTestSession(102, 70);

        await scheduler.CallTempleEventAsync(
            TempleEvent.ForgottenTemple,
            joinWindowSeconds: 300,
            autoJoinSession: null,
            minLevel: 60,
            maxLevel: 83);

        lowPackets.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_BIFROST);

        highPackets.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_BIFROST);
    }

    [Fact]
    public async Task CallTempleEventAsync_OutOfRangeCaller_IsNotAutoJoined()
    {
        var (lowPlayer, _) = CreateTestSession(103, 50);

        await scheduler.CallTempleEventAsync(
            TempleEvent.ForgottenTemple,
            joinWindowSeconds: 300,
            autoJoinSession: lowPlayer,
            minLevel: 60,
            maxLevel: 83);

        scheduler.TryJoinTempleEvent(lowPlayer).Should().Be(TempleJoinResult.LevelTooLow);
    }

    [Fact]
    public async Task CallTempleEventAsync_InRangeCaller_IsAutoJoined()
    {
        var (highPlayer, _) = CreateTestSession(104, 75);

        await scheduler.CallTempleEventAsync(
            TempleEvent.ForgottenTemple,
            joinWindowSeconds: 300,
            autoJoinSession: highPlayer,
            minLevel: 60,
            maxLevel: 83);

        scheduler.TryJoinTempleEvent(highPlayer).Should().Be(TempleJoinResult.AlreadyJoined);
    }

    private static readonly DateTime FridayEightPmBerlin = new(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc);

    private static TempleEventScheduleData Row(int id, TempleEvent contest, byte hour, byte minLevel, byte maxLevel,
        DayOfWeek? day = null, byte minute = 0) =>
        new() { Id = id, Event = contest, Day = day, Hour = hour, Minute = minute, MinLevel = minLevel, MaxLevel = maxLevel, CountdownMinutes = 10 };

    [Fact]
    public async Task TwoEventsDueInTheSameMinuteBothStart()
    {
        gameData.TempleEventSchedules.Returns([
            Row(1, TempleEvent.BorderDefenseWar, 20, 20, 83),
            Row(2, TempleEvent.UnderTheCastle, 20, 70, 83, DayOfWeek.Friday),
        ]);

        await scheduler.TickTempleEventsAsync(FridayEightPmBerlin);

        scheduler.TempleEvents.Select(e => e.Contest).Should().BeEquivalentTo(
            [TempleEvent.BorderDefenseWar, TempleEvent.UnderTheCastle]);
        scheduler.TempleEvents.Should().OnlyContain(e => e.JoinOpen);
    }

    [Fact]
    public async Task EveryShippedScheduleRowStartsItsEventWithinAWeek()
    {
        var rows = new TempleEventScheduleSeed().GetSeedData().ToList();
        gameData.TempleEventSchedules.Returns(rows);
        var start = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        var missed = new List<string>();

        for (var utc = start; utc < start.AddDays(7); utc = utc.AddMinutes(1))
        {
            await scheduler.TickTempleEventsAsync(utc);
            var local = ScheduleTime.Now(settings.Value.Events, utc);
            var running = scheduler.TempleEventsAt(utc);
            foreach (var row in rows.Where(r => r.Matches(local)))
            {
                int window = row.CountdownMinutes * TempleEventRules.SecondsPerMinute;
                if (!running.Any(e => e.Contest == row.Event && e.JoinOpen && e.SecondsLeft == window))
                    missed.Add($"row {row.Id} {row.Event} at {local:ddd HH:mm}");
            }
        }

        rows.Should().NotBeEmpty();
        missed.Should().BeEmpty();
    }

    [Fact]
    public async Task ScheduleRowsAreReadOnTheScheduleClockNotUtc()
    {
        gameData.TempleEventSchedules.Returns([Row(1, TempleEvent.BorderDefenseWar, 18, 20, 83)]);

        await scheduler.TickTempleEventsAsync(FridayEightPmBerlin);

        scheduler.TempleEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task ARunningEventDoesNotHoldBackTheNextOne()
    {
        gameData.TempleEventSchedules.Returns([Row(1, TempleEvent.ForgottenTemple, 20, 60, 83)]);
        await scheduler.TickTempleEventsAsync(FridayEightPmBerlin);
        forgottenTemple.IsActive.Returns(true);
        await scheduler.TickTempleEventsAsync(FridayEightPmBerlin.AddMinutes(TempleEventRules.DefaultCountdownMinutes));

        gameData.TempleEventSchedules.Returns([Row(2, TempleEvent.JuraidMountain, 20, 70, 83, minute: 40)]);
        var juraidMinute = FridayEightPmBerlin.AddMinutes(40);
        await scheduler.TickTempleEventsAsync(juraidMinute);

        scheduler.TempleEvents.Should().HaveCount(2);
        scheduler.TempleEvents.Should().Contain(e => e.Contest == TempleEvent.ForgottenTemple && !e.JoinOpen);
        scheduler.TempleEvents.Should().Contain(e => e.Contest == TempleEvent.JuraidMountain && e.JoinOpen);
    }

    [Fact]
    public async Task AJoinGoesToTheNamedSignUpOrTheNewestOneThePlayerFits()
    {
        var (player, _) = CreateTestSession(105, 75);
        await scheduler.CallTempleEventAsync(TempleEvent.BorderDefenseWar, 300, minLevel: 20, maxLevel: 83);
        await scheduler.CallTempleEventAsync(TempleEvent.ForgottenTemple, 300, minLevel: 46, maxLevel: 59);

        scheduler.SignUpFor(player).Should().Be(TempleEvent.BorderDefenseWar);
        scheduler.TryJoinTempleEvent(player, TempleEvent.ForgottenTemple).Should().Be(TempleJoinResult.LevelTooHigh);
        scheduler.TryJoinTempleEvent(player).Should().Be(TempleJoinResult.Joined);
        scheduler.TempleSignUps(TempleEvent.BorderDefenseWar).Should().Be(((ushort)0, (ushort)1));
    }

    [Fact]
    public async Task ASecondSignUpIsRefusedAsAnotherInstantDungeon()
    {
        var (player, _) = CreateTestSession(106, 75);
        await scheduler.CallTempleEventAsync(TempleEvent.BorderDefenseWar, 300, minLevel: 20, maxLevel: 83);
        await scheduler.CallTempleEventAsync(TempleEvent.UnderTheCastle, 300, minLevel: 70, maxLevel: 83);

        scheduler.TryJoinTempleEvent(player, TempleEvent.UnderTheCastle).Should().Be(TempleJoinResult.Joined);
        scheduler.TryJoinTempleEvent(player, TempleEvent.BorderDefenseWar).Should().Be(TempleJoinResult.OtherDungeon);

        scheduler.LeaveTempleEvent(player.CharacterId).Should().Be(TempleEvent.UnderTheCastle);
        scheduler.TryJoinTempleEvent(player, TempleEvent.BorderDefenseWar).Should().Be(TempleJoinResult.Joined);
    }

    [Fact]
    public async Task CancellingOneEventLeavesTheOthersRunning()
    {
        await scheduler.CallTempleEventAsync(TempleEvent.BorderDefenseWar, 300, minLevel: 20, maxLevel: 83);
        await scheduler.CallTempleEventAsync(TempleEvent.Chaos, 300, minLevel: 1, maxLevel: 83);

        (await scheduler.CancelTempleEventAsync(TempleEvent.Chaos)).Should().BeTrue();

        scheduler.IsTempleEventRunning(TempleEvent.Chaos).Should().BeFalse();
        scheduler.IsTempleEventRunning(TempleEvent.BorderDefenseWar).Should().BeTrue();
        (await scheduler.CancelTempleEventAsync(TempleEvent.Chaos)).Should().BeFalse();
    }
}
