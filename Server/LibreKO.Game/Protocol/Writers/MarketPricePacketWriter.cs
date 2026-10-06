using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol.Writers;

public static class MarketPricePacketWriter
{
    public const byte NoHistory = 0;
    public const byte History = 1;
    public const byte TooSoon = 11;
    public const byte NotPremium = 41;

    public static Packet Result(byte result)
    {
        var packet = new Packet(GameOpcodes.GS_MARKET_PRICE);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet Days(int itemId, MarketPriceSummary summary)
    {
        var packet = Result(History);
        packet.WriteInt(itemId);
        for (var index = 0; index < MarketPriceSummary.DaysShown; index++)
        {
            var day = index < summary.Days.Count ? summary.Days[index] : MarketPriceDayView.Empty;
            packet.WriteLong(day.Average);
            packet.WriteLong(day.Max);
            packet.WriteLong(day.Min);
        }
        packet.WriteInt(summary.Trades);
        packet.WriteUInt((uint)new DateTimeOffset(DateTime.SpecifyKind(summary.LastUpdate, DateTimeKind.Utc)).ToUnixTimeSeconds());
        return packet;
    }
}
