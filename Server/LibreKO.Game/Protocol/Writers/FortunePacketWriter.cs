using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class FortunePacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const int NoReward = 0;

    public static Packet Status(byte sub, bool canDraw)
    {
        var packet = Sub(sub);
        packet.WriteByte(canDraw ? Succeeded : Failed);
        return packet;
    }

    public static Packet DrawResult(byte sub, byte result, int rewardItemId, int rewardGold)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(rewardItemId);
        packet.WriteInt(rewardGold);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_FORTUNE);
        packet.WriteByte(sub);
        return packet;
    }
}
