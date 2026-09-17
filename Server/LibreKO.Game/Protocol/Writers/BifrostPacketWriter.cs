using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class BifrostPacketWriter
{
    public static Packet Remaining(TempleSubOpcode sub, int secondsRemaining)
    {
        var packet = new Packet(GameOpcodes.GS_BIFROST);
        packet.WriteByte((byte)sub);
        packet.WriteInt(secondsRemaining);
        return packet;
    }
}
