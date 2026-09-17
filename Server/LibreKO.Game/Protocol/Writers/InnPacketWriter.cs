using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class InnPacketWriter
{
    public static Packet HomeZone(byte sub, byte result, ushort zoneId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteUShort(zoneId);
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
        var packet = new Packet(GameOpcodes.GS_INN);
        packet.WriteByte(sub);
        return packet;
    }
}
