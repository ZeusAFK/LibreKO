using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ChallengePacketWriter
{
    public static Packet Notice(byte sub) => Sub(sub);

    public static Packet Named(byte sub, string name)
    {
        var packet = Sub(sub);
        packet.WriteSByteString(name);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_CHALLENGE);
        packet.WriteByte(sub);
        return packet;
    }
}
