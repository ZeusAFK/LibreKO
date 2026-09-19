using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class BifrostPacketWriter
{
    public static Packet Remaining(TempleSubOpcode sub, int secondsRemaining, byte eventType = 0)
    {
        var packet = new Packet(GameOpcodes.GS_BIFROST);
        packet.WriteByte((byte)sub);
        packet.WriteInt(secondsRemaining);
        packet.WriteByte(eventType);
        return packet;
    }
}
