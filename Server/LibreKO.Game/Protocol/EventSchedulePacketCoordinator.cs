using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Protocol;

public interface IEventSchedulePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Packet Current();
}

public sealed class EventSchedulePacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    EventSchedulerService scheduler,
    ICollectionRaceService collectionRaces,
    ILotteryService lottery,
    IKrowazService krowaz,
    IBifrostEventService bifrost,
    ICastleSiegeService castleSiege,
    IBowlEventService bowl,
    ScheduleClock scheduleClock,
    IOptions<GameServerSettings> settings) : IEventSchedulePacketCoordinator
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;
    private const int DaysPerWeek = 7;
    private const int MinutesPerWeek = DaysPerWeek * MinutesPerDay;
    private const byte AnyLevel = 0;
    private const byte NoCode = 0;
    private const byte StartMinuteOfHour = 0;
    private const byte FirstSiegeWeekday = 1;

    public const string KrowazName = "Krowaz's Dominion";
    public const string BifrostName = "Bifrost";
    public const string CastleSiegeName = "Castle Siege War";
    public const string BowlName = "Bowl Event";
    private const string BifrostDescription = "Bifrost in Ronark Land";
    private const string CastleSiegeDescription = "Clan war for Delos Castle";

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1 || packet.ReadByte() != EventSchedulePacketWriter.SubSchedule)
            return;

        await session.Client.SendPacket(Current());
    }

    public Packet Current() => EventSchedulePacketWriter.Schedule(
        (int)Math.Clamp(scheduleClock.Offset.TotalSeconds, int.MinValue, int.MaxValue), Now(), Events(DateTime.UtcNow));

    internal List<EventNow> Now()
    {
        var now = new List<EventNow>();
        foreach (var run in scheduler.TempleEvents)
            now.Add(new EventNow(EventScheduleKind.Temple, (byte)run.Contest, TempleEventRules.NameFor(run.Contest),
                run.JoinOpen ? EventSchedulePhase.SignUp : EventSchedulePhase.Running,
                run.SecondsLeft, run.MinLevel, run.MaxLevel));

        foreach (var race in collectionRaces.ActiveRaces)
            now.Add(new EventNow(EventScheduleKind.CollectionRace, NoCode, race.Race.Name, EventSchedulePhase.Running,
                race.RemainingSeconds, race.Race.MinLevel, race.Race.MaxLevel));

        if (lottery.ActiveEvent is { Closed: false } draw)
            now.Add(new EventNow(EventScheduleKind.Lottery, NoCode, draw.EventData.Name, EventSchedulePhase.Running,
                draw.RemainingSeconds, AnyLevel, AnyLevel));

        if (krowaz.IsOpen)
            now.Add(new EventNow(EventScheduleKind.Krowaz, NoCode, KrowazName, EventSchedulePhase.Running,
                krowaz.RemainingSeconds, KrowazService.EntryMinLevel, AnyLevel));

        if (bifrost.IsActive)
            now.Add(new EventNow(EventScheduleKind.Bifrost, NoCode, BifrostName, EventSchedulePhase.Running,
                (int)Math.Min(bifrost.RemainingSecs, int.MaxValue), AnyLevel, AnyLevel));

        if (bowl.IsActive)
            now.Add(new EventNow(EventScheduleKind.Bowl, NoCode, BowlName, EventSchedulePhase.Running,
                (int)bowl.Remaining.TotalSeconds, bowl.Settings.MinLevel, AnyLevel));

        if (castleSiege.Phase == CastleSiegePhase.War)
            now.Add(new EventNow(EventScheduleKind.CastleSiege, NoCode, CastleSiegeName, EventSchedulePhase.Running,
                castleSiege.WarSecondsLeft, AnyLevel, AnyLevel));

        return now;
    }

    internal List<ScheduledEvent> Events(DateTime utcNow)
    {
        var events = settings.Value.Events;
        int offset = ScheduleTime.OffsetMinutes(events, utcNow);
        var list = new List<ScheduledEvent>();

        var templeGroups = (gameDataService.TempleEventSchedules ?? [])
            .OrderBy(s => s.Id)
            .GroupBy(s => (s.Event,
                Min: s.MinLevel > 0 ? s.MinLevel : TempleEventRules.DefaultMinLevel(s.Event),
                Max: s.MaxLevel > 0 ? s.MaxLevel : TempleEventRules.DefaultMaxLevel(s.Event)));
        foreach (var group in templeGroups)
            list.Add(new ScheduledEvent(EventScheduleKind.Temple, TempleEventRules.NameFor(group.Key.Event),
                TempleEventRules.DescriptionFor(group.Key.Event), group.Key.Min, group.Key.Max,
                group.Select(s => Slot(s.Day, s.Hour, s.Minute, offset)).ToList()));

        if (events.KrowazStartHours.Length > 0)
            list.Add(new ScheduledEvent(EventScheduleKind.Krowaz, KrowazName,
                $"Opens for {KrowazService.OpenMinutes} minutes", KrowazService.EntryMinLevel, AnyLevel,
                Hourly(events.KrowazStartHours, offset)));

        if (events.BifrostStartHours.Length > 0)
            list.Add(new ScheduledEvent(EventScheduleKind.Bifrost, BifrostName, BifrostDescription, AnyLevel, AnyLevel,
                Hourly(events.BifrostStartHours, offset)));

        var bowlSettings = bowl.Settings;
        if (bowlSettings.Enabled && bowlSettings.StartHours.Length > 0)
            list.Add(new ScheduledEvent(EventScheduleKind.Bowl, BowlName,
                $"Rewards for staying active in the Bowl of Ronark Land, {bowlSettings.DefaultDurationMinutes} minutes",
                bowlSettings.MinLevel, AnyLevel, Hourly(bowlSettings.StartHours, offset)));

        if (gameDataService.SiegeWarfare is { } siege && SiegeRules.IsWeekday(siege.WarDay))
            list.Add(new ScheduledEvent(EventScheduleKind.CastleSiege, CastleSiegeName, CastleSiegeDescription,
                AnyLevel, AnyLevel,
                [Slot((DayOfWeek)(siege.WarDay - FirstSiegeWeekday), siege.WarTime, siege.WarMinute, offset)]));

        foreach (var group in gameDataService.CollectionRaceSchedulesByRace)
        {
            if (!gameDataService.CollectionRaceTable.TryGetValue(group.Key, out var race))
                continue;
            list.Add(new ScheduledEvent(EventScheduleKind.CollectionRace, race.Name,
                $"Collection Race, {race.DurationMinutes} minutes, first {race.MaxWinners} win", race.MinLevel, race.MaxLevel,
                group.Select(s => Slot(s.Day, s.Hour, s.Minute, 0)).ToList()));
        }

        foreach (var group in gameDataService.LotterySchedulesByEvent)
        {
            if (!gameDataService.LotteryEventTable.TryGetValue(group.Key, out var draw))
                continue;
            list.Add(new ScheduledEvent(EventScheduleKind.Lottery, draw.Name,
                $"Lottery draw, {draw.DurationMinutes} minutes", AnyLevel, AnyLevel,
                group.Select(s => Slot(s.Day, s.Hour, s.Minute, 0)).ToList()));
        }

        return list;
    }

    private static List<EventSlot> Hourly(IEnumerable<int> hours, int offsetMinutes) =>
        hours.Order().Select(h => Slot(null, (byte)h, StartMinuteOfHour, offsetMinutes)).ToList();

    internal static EventSlot Slot(DayOfWeek? day, byte hour, byte minute, int offsetMinutes)
    {
        if (day == null)
        {
            int minuteOfDay = Wrap(hour * MinutesPerHour + minute - offsetMinutes, MinutesPerDay);
            return new EventSlot(EventSchedulePacketWriter.EveryDay,
                (byte)(minuteOfDay / MinutesPerHour), (byte)(minuteOfDay % MinutesPerHour));
        }

        int minuteOfWeek = Wrap((int)day.Value * MinutesPerDay + hour * MinutesPerHour + minute - offsetMinutes,
            MinutesPerWeek);
        int dayMinutes = minuteOfWeek % MinutesPerDay;
        return new EventSlot((byte)(minuteOfWeek / MinutesPerDay),
            (byte)(dayMinutes / MinutesPerHour), (byte)(dayMinutes % MinutesPerHour));
    }

    private static int Wrap(int value, int span) => ((value % span) + span) % span;
}
