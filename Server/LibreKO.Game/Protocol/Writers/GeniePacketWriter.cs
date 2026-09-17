using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class GeniePacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const int NoReward = 0;

    public static Packet Status(byte sub, string tip, bool rewardAvailable)
    {
        var packet = Sub(sub);
        packet.WriteSByteString(tip);
        packet.WriteByte(rewardAvailable ? Succeeded : Failed);
        return packet;
    }

    public static Packet ClaimResult(byte sub, byte result, int rewardGold)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(rewardGold);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_GENIE);
        packet.WriteByte(sub);
        return packet;
    }
}
