using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class RingUpgradePacketWriter
{
    public static Packet Status(RingUpgradeSubOpcode sub, byte successRate)
    {
        var packet = Sub(sub);
        packet.WriteByte(successRate);
        return packet;
    }

    public static Packet UpgradeResult(RingUpgradeSubOpcode sub, RingUpgradeResult result, byte currentPlus)
    {
        var packet = Sub(sub);
        packet.WriteByte((byte)result);
        packet.WriteByte(currentPlus);
        return packet;
    }

    private static Packet Sub(RingUpgradeSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_RING_UPGRADE);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
