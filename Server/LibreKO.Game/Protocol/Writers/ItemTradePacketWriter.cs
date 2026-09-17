using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ItemTradePacketWriter
{
    public static Packet Failed(ItemTradeRefusal reason)
    {
        var packet = Sub(ItemTradeResult.Refused);
        packet.WriteByte((byte)reason);
        return packet;
    }

    public static Packet Moved() => Sub(ItemTradeResult.Moved);

    public static Packet Traded(int balance, int price, byte? loyaltySellingGroup = null)
    {
        var packet = Sub(ItemTradeResult.Traded);
        packet.WriteInt(balance);
        packet.WriteInt(price);
        if (loyaltySellingGroup.HasValue)
            packet.WriteByte(loyaltySellingGroup.Value);
        return packet;
    }

    private static Packet Sub(ItemTradeResult result)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte((byte)result);
        return packet;
    }
}
