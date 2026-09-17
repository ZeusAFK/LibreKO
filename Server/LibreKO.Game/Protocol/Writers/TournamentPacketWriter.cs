using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class TournamentPacketWriter
{
    public const byte NotRegistered = 0;
    public const byte Registered = 1;

    public static Packet Status(byte sub, bool registered, ushort participantCount, byte round)
    {
        var packet = Sub(sub);
        packet.WriteByte(registered ? Registered : NotRegistered);
        packet.WriteUShort(participantCount);
        packet.WriteByte(round);
        return packet;
    }

    public static Packet Result(byte sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_TOURNAMENT);
        packet.WriteByte(sub);
        return packet;
    }
}
