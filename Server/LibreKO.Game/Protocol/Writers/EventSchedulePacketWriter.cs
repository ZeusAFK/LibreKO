using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum EventScheduleKind : byte
{
    Temple = 1,
    CollectionRace = 2,
    Lottery = 3,
    Krowaz = 4,
    Bifrost = 5,
    CastleSiege = 6,
    Bowl = 7,
}

public enum EventSchedulePhase : byte
{
    SignUp = 1,
    Running = 2,
}

public readonly record struct EventNow(
    EventScheduleKind Kind, byte Code, string Name, EventSchedulePhase Phase, int SecondsLeft, byte MinLevel, byte MaxLevel);

public readonly record struct EventSlot(byte Day, byte HourUtc, byte MinuteUtc);

public sealed record ScheduledEvent(
    EventScheduleKind Kind, string Name, string Description, byte MinLevel, byte MaxLevel, IReadOnlyList<EventSlot> Slots);

public static class EventSchedulePacketWriter
{
    public const byte SubSchedule = 1;
    public const byte EveryDay = 7;

    public static Packet Schedule(int clockOffsetSeconds, IReadOnlyList<EventNow> now, IReadOnlyList<ScheduledEvent> events)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_SCHEDULE);
        packet.WriteByte(SubSchedule);
        packet.WriteInt(clockOffsetSeconds);
        packet.WriteByte((byte)Math.Min(now.Count, byte.MaxValue));
        foreach (var entry in now.Take(byte.MaxValue))
        {
            packet.WriteByte((byte)entry.Kind);
            packet.WriteByte(entry.Code);
            packet.WriteString(entry.Name);
            packet.WriteByte((byte)entry.Phase);
            packet.WriteInt(entry.SecondsLeft);
            packet.WriteByte(entry.MinLevel);
            packet.WriteByte(entry.MaxLevel);
        }

        packet.WriteUShort((ushort)Math.Min(events.Count, ushort.MaxValue));
        foreach (var scheduled in events.Take(ushort.MaxValue))
        {
            packet.WriteByte((byte)scheduled.Kind);
            packet.WriteString(scheduled.Name);
            packet.WriteString(scheduled.Description);
            packet.WriteByte(scheduled.MinLevel);
            packet.WriteByte(scheduled.MaxLevel);
            packet.WriteByte((byte)Math.Min(scheduled.Slots.Count, byte.MaxValue));
            foreach (var slot in scheduled.Slots.Take(byte.MaxValue))
            {
                packet.WriteByte(slot.Day);
                packet.WriteByte(slot.HourUtc);
                packet.WriteByte(slot.MinuteUtc);
            }
        }

        return packet;
    }
}
