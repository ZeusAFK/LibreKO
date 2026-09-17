using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class AttendancePacketWriter
{
    public readonly record struct SlotState(int Slot, byte State);

    public static Packet Result(EventBoardSubOpcode sub, uint channel, uint result)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte((byte)sub);
        packet.WriteUInt(channel);
        packet.WriteUInt(result);
        return packet;
    }

    public static Packet Board(EventBoardSubOpcode sub, uint channel, uint result,
        IReadOnlyCollection<SlotState> dailySlots,
        IReadOnlyCollection<SlotState> bonusSlots)
    {
        var packet = Result(sub, channel, result);
        packet.WriteUInt(channel);

        WriteSlots(packet, dailySlots);
        WriteSlots(packet, bonusSlots);

        packet.WriteUInt(channel);
        return packet;
    }

    private static void WriteSlots(Packet packet, IReadOnlyCollection<SlotState> slots)
    {
        packet.WriteUShort((ushort)slots.Count);
        foreach (var slot in slots)
        {
            packet.WriteInt(slot.Slot);
            packet.WriteByte(slot.State);
        }
    }
}
