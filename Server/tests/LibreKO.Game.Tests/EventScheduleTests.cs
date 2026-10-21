using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class EventScheduleTests
{
    private const int ThreeHoursAhead = 180;
    private const int BerlinSummerOffset = 120;
    private const int BerlinWinterOffset = 60;
    private const int FourteenHours = 14 * 3600;
    private const int SecondsPerMinute = 60;

    [Fact]
    public void ADailyLocalHourIsSentInUtc()
    {
        var slot = EventSchedulePacketCoordinator.Slot(null, 1, 30, ThreeHoursAhead);

        slot.Should().Be(new EventSlot(EventSchedulePacketWriter.EveryDay, 22, 30));
    }

    [Fact]
    public void AWeeklyLocalTimeCrossesBackIntoTheDayBefore()
    {
        var slot = EventSchedulePacketCoordinator.Slot(DayOfWeek.Sunday, 1, 0, ThreeHoursAhead);

        slot.Should().Be(new EventSlot((byte)DayOfWeek.Saturday, 22, 0));
    }

    [Fact]
    public void UtcSchedulesPassThrough()
    {
        var slot = EventSchedulePacketCoordinator.Slot(DayOfWeek.Wednesday, 11, 5, 0);

        slot.Should().Be(new EventSlot((byte)DayOfWeek.Wednesday, 11, 5));
    }

    [Fact]
    public void TheDefaultScheduleClockFollowsBerlinSummerAndWinterTime()
    {
        var events = new EventSettings();

        ScheduleTime.OffsetMinutes(events, new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)).Should().Be(BerlinSummerOffset);
        ScheduleTime.OffsetMinutes(events, new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Should().Be(BerlinWinterOffset);
        ScheduleTime.Now(events, new DateTime(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc)).Hour.Should().Be(20);
    }

    [Fact]
    public void AnUnknownTimeZoneFallsBackToUtc()
    {
        var events = new EventSettings { ScheduleTimeZone = "Nowhere/Atlantis" };

        ScheduleTime.TryZone(events.ScheduleTimeZone, out _).Should().BeFalse();
        ScheduleTime.OffsetMinutes(events, DateTime.UtcNow).Should().Be(0);
    }

    [Fact]
    public void TheGmClockLandsOnTheRequestedScheduleTimeAndResets()
    {
        var events = new EventSettings();
        var clock = new ScheduleClock();

        clock.MoveTo(events, 20, 0);
        var moved = ScheduleTime.Now(events, clock.UtcNow);
        (moved.Hour, moved.Minute).Should().Be((20, 0));
        moved.Date.Should().Be(ScheduleTime.Now(events, DateTime.UtcNow).Date);

        clock.Reset();
        clock.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task AMovedClockStartsTheEventsDueAtItsTime()
    {
        var settings = new GameServerSettings();
        var clock = new ScheduleClock();
        var data = Substitute.For<IGameDataService>();
        data.TempleEventSchedules.Returns([new TempleEventScheduleData
            { Id = 1, Event = TempleEvent.BorderDefenseWar, Hour = 20, Minute = 0, MinLevel = 20, MaxLevel = 83, CountdownMinutes = 10 }]);
        var scheduler = new EventSchedulerService(new SessionManager(), Options.Create(settings),
            Substitute.For<IZoneTransitionService>(), Substitute.For<ICollectionRaceService>(),
            Substitute.For<ILotteryService>(), Substitute.For<IJuraidMountainService>(),
            Substitute.For<IBorderDefenseWarService>(), Substitute.For<IChaosDungeonService>(),
            Substitute.For<IUnderTheCastleService>(), Substitute.For<IForgottenTempleService>(),
            Substitute.For<IKrowazService>(), Substitute.For<INereidMonumentService>(),
            Substitute.For<IBowlEventService>(),
            data, clock, NullLogger<EventSchedulerService>.Instance);

        clock.MoveTo(settings.Events, 20, 0);
        await scheduler.TickTempleEventsAsync(DateTime.UtcNow, clock.UtcNow);

        scheduler.IsTempleEventRunning(TempleEvent.BorderDefenseWar).Should().BeTrue();
        scheduler.TempleEvents.Single().SecondsLeft.Should().BeCloseTo(10 * SecondsPerMinute, 2);
    }

    [Fact]
    public void BifrostStartsOnceAtEachListedHour()
    {
        var settings = new GameServerSettings();
        var bifrost = new BifrostEventService(new SessionManager(), Options.Create(settings), new ScheduleClock(), Substitute.For<IZoneTransitionService>(),
            NullLogger<BifrostEventService>.Instance);
        var opening = new DateTime(2026, 10, 12, settings.Events.BifrostStartHours[0], 0, 0);

        bifrost.TickSchedule(opening.AddMinutes(-1));
        bifrost.IsActive.Should().BeFalse();

        bifrost.TickSchedule(opening);
        bifrost.IsActive.Should().BeTrue();

        bifrost.Close();
        bifrost.TickSchedule(opening);
        bifrost.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ThePacketCarriesTheClockShiftTheLiveEntriesAndEachEventWithItsTimes()
    {
        var packet = EventSchedulePacketWriter.Schedule(FourteenHours,
            [new EventNow(EventScheduleKind.Temple, 2, "Border Defense War", EventSchedulePhase.SignUp, 1800, 20, 83)],
            [new ScheduledEvent(EventScheduleKind.Temple, "Chaos Dungeon", "Arena", 1, 83,
                [new EventSlot(EventSchedulePacketWriter.EveryDay, 9, 0), new EventSlot((byte)DayOfWeek.Friday, 16, 30)])]);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_EVENT_SCHEDULE);
        packet.ResetOffset();
        packet.ReadByte().Should().Be(EventSchedulePacketWriter.SubSchedule);
        packet.ReadInt().Should().Be(FourteenHours);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be((byte)EventScheduleKind.Temple);
        packet.ReadByte().Should().Be(2);
        packet.ReadString().Should().Be("Border Defense War");
        packet.ReadByte().Should().Be((byte)EventSchedulePhase.SignUp);
        packet.ReadInt().Should().Be(1800);
        packet.ReadByte().Should().Be(20);
        packet.ReadByte().Should().Be(83);
        packet.ReadUShort().Should().Be(1);
        packet.ReadByte().Should().Be((byte)EventScheduleKind.Temple);
        packet.ReadString().Should().Be("Chaos Dungeon");
        packet.ReadString().Should().Be("Arena");
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(83);
        packet.ReadByte().Should().Be(2);
        (packet.ReadByte(), packet.ReadByte(), packet.ReadByte()).Should().Be((EventSchedulePacketWriter.EveryDay, (byte)9, (byte)0));
        (packet.ReadByte(), packet.ReadByte(), packet.ReadByte()).Should().Be(((byte)DayOfWeek.Friday, (byte)16, (byte)30));
        packet.RemainingBytes.Should().Be(0);
    }
}
