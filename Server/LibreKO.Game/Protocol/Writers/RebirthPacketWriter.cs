using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class RebirthPacketWriter
{
    public static Packet Activate(RebirthSubOpcode sub) => Sub(sub);

    public static Packet Result(RebirthSubOpcode sub, byte resultCode)
    {
        var packet = Sub(sub);
        packet.WriteByte(resultCode);
        return packet;
    }

    public static Packet Progress(RebirthSubOpcode sub, int levelOffset, int current, int max)
    {
        var packet = Sub(sub);
        packet.WriteInt(levelOffset);
        packet.WriteInt(current);
        packet.WriteInt(max);
        return packet;
    }

    public static Packet Complete(RebirthSubOpcode sub, int rebirthLevel)
    {
        var packet = Sub(sub);
        packet.WriteInt(rebirthLevel);
        return packet;
    }

    private static Packet Sub(RebirthSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_REBIRTH);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
