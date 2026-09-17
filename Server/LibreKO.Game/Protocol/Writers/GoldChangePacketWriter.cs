using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class GoldChangePacketWriter
{
    public const byte Gained = 1;
    public const byte Spent = 2;

    public static Packet Change(byte changeType, int amount, int total)
    {
        var packet = new Packet(GameOpcodes.GS_GOLD_CHANGE);
        packet.WriteByte(changeType);
        packet.WriteInt(amount);
        packet.WriteInt(total);
        return packet;
    }
}
