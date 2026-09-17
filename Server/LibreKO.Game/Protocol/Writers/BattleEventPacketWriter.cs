using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class BattleEventPacketWriter
{
    public static Packet Notice(byte sub, byte value)
    {
        var packet = Sub(sub);
        packet.WriteByte(value);
        return packet;
    }

    public static Packet Opened(byte sub, byte zoneId, short durationMinutes)
    {
        var packet = Notice(sub, zoneId);
        packet.WriteShort(durationMinutes);
        return packet;
    }

    public static Packet Banished(byte sub) => Sub(sub);

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_BATTLE_EVENT);
        packet.WriteByte(sub);
        return packet;
    }
}
