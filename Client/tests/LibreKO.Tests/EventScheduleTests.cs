using System;
using System.Collections.Generic;
using System.Linq;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class EventScheduleTests
{
    private const int UtcMinusFour = -240;
    private static readonly DateTime Wednesday1000 = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);

    private static ScheduledEvent Event(string name, int minLevel, int maxLevel, params EventSlot[] slots) =>
        new(EventKind.Temple, name, "", minLevel, maxLevel, slots);

    private static EventSchedule Schedule(IReadOnlyList<EventNow> now, params ScheduledEvent[] events) =>
        new(TimeSpan.Zero, now, events, Wednesday1000);

    [Fact]
    public void ADailySlotThatPassedMovesToTomorrow()
    {
        var slot = new EventSlot(EventSchedule.EveryDay, 9, 0);

        Assert.Equal(Wednesday1000.Date.AddDays(1).AddHours(9), EventSchedule.NextStartUtc(slot, Wednesday1000));
    }

    [Fact]
    public void AWeeklySlotFindsItsNextWeekday()
    {
        var friday = new EventSlot((int)DayOfWeek.Friday, 21, 0);

        Assert.Equal(new DateTime(2026, 10, 9, 21, 0, 0, DateTimeKind.Utc), EventSchedule.NextStartUtc(friday, Wednesday1000));
    }

    [Fact]
    public void AUtcSlotShiftsIntoLocalTimeAcrossMidnightAndTheWeek()
    {
        Assert.Equal(new EventClock(EventSchedule.EveryDay, 22, 0),
            EventSchedule.Shift(new EventSlot(EventSchedule.EveryDay, 2, 0), UtcMinusFour));
        Assert.Equal(new EventClock((int)DayOfWeek.Saturday, 21, 30),
            EventSchedule.Shift(new EventSlot((int)DayOfWeek.Sunday, 1, 30), UtcMinusFour));
    }

    [Fact]
    public void WeekdayPillsShowTheLocalDays()
    {
        var siege = Event("Castle Siege War", 0, 0, new EventSlot((int)DayOfWeek.Sunday, 1, 30));

        Assert.Equal(new[] { (int)DayOfWeek.Saturday }, EventSchedule.Weekdays(siege, UtcMinusFour));
    }

    [Fact]
    public void TheIconPulsesOnlyForAFittingEventWithinTwentyMinutes()
    {
        var schedule = Schedule([], Event("FT", 60, 83, new EventSlot(EventSchedule.EveryDay, 10, 15)));

        Assert.True(schedule.AlertDue(65, Wednesday1000, TimeSpan.FromMinutes(20)));
        Assert.False(schedule.AlertDue(40, Wednesday1000, TimeSpan.FromMinutes(20)));
        Assert.False(schedule.AlertDue(65, Wednesday1000, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void CardsPutLiveEventsFirstThenTheNextToStart()
    {
        var live = new EventNow(EventKind.Temple, 2, "BDW", EventPhase.SignUp, 300, 20, 83);
        var schedule = Schedule([live],
            Event("Late", 0, 0, new EventSlot(EventSchedule.EveryDay, 22, 0)),
            Event("BDW", 20, 83, new EventSlot(EventSchedule.EveryDay, 9, 0)),
            Event("Early", 0, 0, new EventSlot(EventSchedule.EveryDay, 11, 0)));

        var cards = schedule.Cards(Wednesday1000);

        Assert.Equal(new[] { "BDW", "Early", "Late" }, cards.Select(c => c.Name));
        Assert.Equal(live, cards[0].Live);
    }

    [Fact]
    public void AGmMovedClockCountsDownFromTheMovedTime()
    {
        var bdw = Event("BDW", 20, 83, new EventSlot(EventSchedule.EveryDay, 20, 0));
        var moved = new EventSchedule(TimeSpan.FromHours(10), [], new[] { bdw }, Wednesday1000);

        Assert.Equal(TimeSpan.Zero, moved.UntilStart(bdw, Wednesday1000));
        Assert.True(moved.AlertDue(60, Wednesday1000, TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.FromHours(10), Schedule([], bdw).UntilStart(bdw, Wednesday1000));
    }

    [Fact]
    public void ALiveEntryWithoutAScheduleStillGetsACard()
    {
        var gmCalled = new EventNow(EventKind.Temple, 1, "Chaos Dungeon", EventPhase.SignUp, 30, 1, 83);

        var cards = Schedule([gmCalled]).Cards(Wednesday1000);

        Assert.Single(cards);
        Assert.Null(cards[0].Event);
        Assert.Equal("Chaos Dungeon", cards[0].Name);
    }

    [Fact]
    public void ARunningEventCountsDownFromWhenItArrived()
    {
        var entry = new EventNow(EventKind.Temple, 2, "BDW", EventPhase.SignUp, 100, 20, 83);

        Assert.Equal(70, Schedule([entry]).SecondsLeft(entry, Wednesday1000.AddSeconds(30)));
    }

    [Fact]
    public void TheReaderTakesTheServersLayout()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_SCHEDULE);
        p.WriteByte(1);
        p.WriteInt(3600);
        p.WriteByte(1);
        p.WriteByte((byte)EventKind.Temple);
        p.WriteByte((byte)TempleEventType.BorderDefenseWar);
        p.WriteString("Border Defense War");
        p.WriteByte((byte)EventPhase.SignUp);
        p.WriteInt(1800);
        p.WriteByte(20);
        p.WriteByte(83);
        p.WriteUShort(1);
        p.WriteByte((byte)EventKind.Temple);
        p.WriteString("Chaos Dungeon");
        p.WriteString("Arena");
        p.WriteByte(1);
        p.WriteByte(83);
        p.WriteByte(2);
        p.WriteByte(EventSchedule.EveryDay);
        p.WriteByte(9);
        p.WriteByte(0);
        p.WriteByte((byte)DayOfWeek.Friday);
        p.WriteByte(16);
        p.WriteByte(30);
        p.ResetOffset();

        var schedule = EventScheduleWire.Read(p);

        Assert.NotNull(schedule);
        Assert.Equal(TimeSpan.FromHours(1), schedule!.ClockOffset);
        Assert.Equal(new EventNow(EventKind.Temple, (int)TempleEventType.BorderDefenseWar, "Border Defense War",
            EventPhase.SignUp, 1800, 20, 83), schedule!.Now.Single());
        var chaos = schedule.Events.Single();
        Assert.Equal(("Chaos Dungeon", "Arena", 1, 83), (chaos.Name, chaos.Description, chaos.MinLevel, chaos.MaxLevel));
        Assert.Equal(new[] { new EventSlot(EventSchedule.EveryDay, 9, 0), new EventSlot((int)DayOfWeek.Friday, 16, 30) }, chaos.Slots);
    }
}
