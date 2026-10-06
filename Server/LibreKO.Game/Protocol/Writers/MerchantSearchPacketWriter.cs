using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public sealed record MerchantSearchStall(int SellerId, string Name, MerchantMode Mode, IReadOnlyList<MerchantItem?> Items);

public static class MerchantSearchPacketWriter
{
    public const byte Move = 2;
    public const byte Open = 5;
    public const byte Results = 6;

    public const byte Refused = 0;
    public const byte Accepted = 1;
    public const byte MoreToCome = 2;
    public const byte LastChunk = 3;
    public const byte CannotUseItem = 3;

    private static Packet Start(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)MerchantSubOpcode.OfficialList);
        packet.WriteByte(sub);
        return packet;
    }

    public static Packet Result(byte sub, byte result)
    {
        var packet = Start(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet ResultsRefused() => WithCode(Start(Results), Refused, CannotUseItem);

    private static Packet WithCode(Packet packet, byte result, byte code)
    {
        packet.WriteByte(result);
        packet.WriteByte(code);
        return packet;
    }

    public static Packet Chunk(bool last, int nextCursor, IReadOnlyList<MerchantSearchStall> stalls)
    {
        var packet = Start(Results);
        packet.WriteByte(last ? LastChunk : MoreToCome);
        packet.WriteInt(nextCursor);
        packet.WriteShort((short)stalls.Count);
        foreach (var stall in stalls)
        {
            packet.WriteInt(stall.SellerId);
            packet.WriteSByteString(stall.Name);
            packet.WriteByte((byte)stall.Mode);
            for (var slot = 0; slot < TradeState.MerchantSlots; slot++)
            {
                var item = slot < stall.Items.Count ? stall.Items[slot] : null;
                var listed = item is { IsEmpty: false };
                packet.WriteInt(listed ? item!.ItemId : 0);
                packet.WriteInt(listed ? item!.Price : 0);
            }
        }
        return packet;
    }
}
