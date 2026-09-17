using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ForcesPacketWriter
{
    public const byte NotJoined = 0;
    public const byte Joined = 1;

    public static Packet Result(byte sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet Status(byte sub, bool joined, int points, byte rank, byte angerPercent)
    {
        var packet = Result(sub, joined ? Joined : NotJoined);
        packet.WriteInt(joined ? points : 0);
        packet.WriteByte(joined ? rank : (byte)0);
        packet.WriteByte(joined ? angerPercent : (byte)0);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_FORCES);
        packet.WriteByte(sub);
        return packet;
    }
}
