using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class AuctionPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public readonly record struct Lot(
        int AuctionId, int SellerId, string SellerName, int ItemId, int Count, int CurrentBid, int Buyout);

    public static Packet Listing(byte sub, IReadOnlyCollection<Lot> lots)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)lots.Count);

        foreach (var lot in lots)
        {
            packet.WriteInt(lot.AuctionId);
            packet.WriteInt(lot.SellerId);
            packet.WriteSByteString(lot.SellerName);
            packet.WriteInt(lot.ItemId);
            packet.WriteInt(lot.Count);
            packet.WriteInt(lot.CurrentBid);
            packet.WriteInt(lot.Buyout);
        }

        return packet;
    }

    public static Packet Registered(byte sub, byte result, int auctionId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(auctionId);
        return packet;
    }

    public static Packet BidResult(byte sub, byte result, int auctionId, int amount)
    {
        var packet = Registered(sub, result, auctionId);
        packet.WriteInt(amount);
        return packet;
    }

    public static Packet Cancelled(byte sub, byte result, int auctionId) =>
        Registered(sub, result, auctionId);

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_AUCTION);
        packet.WriteByte(sub);
        return packet;
    }
}
