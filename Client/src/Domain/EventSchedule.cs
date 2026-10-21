using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public enum EventKind : byte { Temple = 1, CollectionRace = 2, Lottery = 3, Krowaz = 4, Bifrost = 5, CastleSiege = 6, Bowl = 7 }

public enum EventPhase : byte { SignUp = 1, Running = 2 }

public readonly record struct EventNow(EventKind Kind, int Code, string Name, EventPhase Phase, int SecondsLeft, int MinLevel, int MaxLevel);

public readonly record struct EventSlot(int Day, int Hour, int Minute);

public readonly record struct EventClock(int Day, int Hour, int Minute)
{
    public bool Daily => Day == EventSchedule.EveryDay;
}

public sealed record ScheduledEvent(EventKind Kind, string Name, string Description, int MinLevel, int MaxLevel, IReadOnlyList<EventSlot> Slots)
{
    public bool Weekly => Slots.Any(s => s.Day != EventSchedule.EveryDay);

    public DateTime NextStartUtc(DateTime utcNow) =>
        Slots.Count == 0 ? DateTime.MaxValue : Slots.Min(s => EventSchedule.NextStartUtc(s, utcNow));

    public bool Matches(EventNow entry) =>
        entry.Kind == Kind && entry.Name == Name && entry.MinLevel == MinLevel && entry.MaxLevel == MaxLevel;
}

public readonly record struct EventCard(ScheduledEvent? Event, EventNow? Live)
{
    public EventKind Kind => Event?.Kind ?? Live!.Value.Kind;
    public string Name => Event?.Name ?? Live!.Value.Name;
    public string Description => Event?.Description ?? "";
    public int MinLevel => Event?.MinLevel ?? Live!.Value.MinLevel;
    public int MaxLevel => Event?.MaxLevel ?? Live!.Value.MaxLevel;
}

public sealed class EventSchedule
{
    public const int EveryDay = 7;
    private const int DaysPerWeek = 7;
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;
    private const int MinutesPerWeek = DaysPerWeek * MinutesPerDay;

    public TimeSpan ClockOffset { get; }
    public IReadOnlyList<EventNow> Now { get; }
    public IReadOnlyList<ScheduledEvent> Events { get; }
    public DateTime ReceivedUtc { get; }

    public EventSchedule(TimeSpan clockOffset, IReadOnlyList<EventNow> now, IReadOnlyList<ScheduledEvent> events, DateTime receivedUtc)
    {
        ClockOffset = clockOffset;
        Now = now;
        Events = events;
        ReceivedUtc = receivedUtc;
    }

    public DateTime ClockNow(DateTime utcNow) => utcNow + ClockOffset;

    public TimeSpan UntilStart(ScheduledEvent scheduled, DateTime utcNow)
    {
        var clockNow = ClockNow(utcNow);
        return scheduled.NextStartUtc(clockNow) - clockNow;
    }

    public int SecondsLeft(EventNow entry, DateTime utcNow) =>
        Math.Max(0, entry.SecondsLeft - (int)(utcNow - ReceivedUtc).TotalSeconds);

    public static bool Fits(int level, int minLevel, int maxLevel) =>
        (minLevel <= 0 || level >= minLevel) && (maxLevel <= 0 || level <= maxLevel);

    public static DateTime NextStartUtc(EventSlot slot, DateTime utcNow)
    {
        var today = utcNow.Date.AddHours(slot.Hour).AddMinutes(slot.Minute);
        if (slot.Day == EveryDay)
            return today >= utcNow ? today : today.AddDays(1);
        int ahead = ((slot.Day - (int)utcNow.DayOfWeek) % DaysPerWeek + DaysPerWeek) % DaysPerWeek;
        var start = today.AddDays(ahead);
        return start >= utcNow ? start : start.AddDays(DaysPerWeek);
    }

    public static EventClock Shift(EventSlot slot, int offsetMinutes)
    {
        if (slot.Day == EveryDay)
        {
            int minuteOfDay = Wrap(slot.Hour * MinutesPerHour + slot.Minute + offsetMinutes, MinutesPerDay);
            return new EventClock(EveryDay, minuteOfDay / MinutesPerHour, minuteOfDay % MinutesPerHour);
        }

        int minuteOfWeek = Wrap(slot.Day * MinutesPerDay + slot.Hour * MinutesPerHour + slot.Minute + offsetMinutes, MinutesPerWeek);
        int dayMinutes = minuteOfWeek % MinutesPerDay;
        return new EventClock(minuteOfWeek / MinutesPerDay, dayMinutes / MinutesPerHour, dayMinutes % MinutesPerHour);
    }

    public static IReadOnlyCollection<int> Weekdays(ScheduledEvent scheduled, int offsetMinutes) =>
        scheduled.Slots.Where(s => s.Day != EveryDay).Select(s => Shift(s, offsetMinutes).Day).ToHashSet();

    public List<EventCard> Cards(DateTime utcNow)
    {
        var cards = new List<EventCard>();
        var claimed = new HashSet<int>();
        foreach (var scheduled in Events)
        {
            int live = -1;
            for (int i = 0; i < Now.Count; i++)
                if (!claimed.Contains(i) && scheduled.Matches(Now[i])) { live = i; break; }
            if (live >= 0) claimed.Add(live);
            cards.Add(new EventCard(scheduled, live >= 0 ? Now[live] : null));
        }
        for (int i = 0; i < Now.Count; i++)
            if (!claimed.Contains(i)) cards.Add(new EventCard(null, Now[i]));

        return cards
            .OrderBy(c => c.Live == null ? 1 : 0)
            .ThenBy(c => c.Live is { } entry ? SecondsLeft(entry, utcNow) : 0)
            .ThenBy(c => c.Event is { } scheduled ? UntilStart(scheduled, utcNow) : TimeSpan.MaxValue)
            .ToList();
    }

    public bool AlertDue(int level, DateTime utcNow, TimeSpan lead)
    {
        foreach (var scheduled in Events)
        {
            if (!Fits(level, scheduled.MinLevel, scheduled.MaxLevel)) continue;
            if (UntilStart(scheduled, utcNow) <= lead) return true;
        }
        return false;
    }

    private static int Wrap(int value, int span) => ((value % span) + span) % span;
}
