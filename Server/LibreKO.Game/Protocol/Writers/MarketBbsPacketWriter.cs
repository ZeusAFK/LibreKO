using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class MarketBbsPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public readonly record struct Advert(
        int AdId,
        int SellerId,
        string Seller,
        int ItemId,
        int Price,
        ushort Count,
        byte BuyType,
        int RemainingDays);

    public static Packet Result(byte sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet Registered(byte sub, byte result, int adId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(adId);
        return packet;
    }

    public static Packet AdvertList(byte sub, IReadOnlyCollection<Advert> adverts)
    {
        var packet = Sub(sub);
        packet.WriteByte(Succeeded);
        packet.WriteUShort((ushort)adverts.Count);
        foreach (var advert in adverts)
        {
            packet.WriteInt(advert.AdId);
            packet.WriteInt(advert.SellerId);
            packet.WriteSByteString(advert.Seller);
            packet.WriteInt(advert.ItemId);
            packet.WriteInt(advert.Price);
            packet.WriteUShort(advert.Count);
            packet.WriteByte(advert.BuyType);
            packet.WriteInt(advert.RemainingDays);
        }
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_MARKET_BBS);
        packet.WriteByte(sub);
        return packet;
    }
}
