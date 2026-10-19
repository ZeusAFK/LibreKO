namespace LibreKO.Network.Readers;

internal sealed class MarketPriceReader : IPacketReader
{
    private readonly MarketPriceSession marketPrice;

    public MarketPriceReader(MarketPriceSession marketPrice)
    {
        this.marketPrice = marketPrice;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_MARKET_PRICE, HandleMarketPrice);
    }

    private void HandleMarketPrice(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int result = p.ReadByte();
        if (result != MarketPrice.History)
        {
            marketPrice.RaiseMarketPrice(new MarketPriceReply(result, 0, Array.Empty<MarketPriceDay>(), 0, default));
            return;
        }

        int itemId = p.ReadInt();
        var days = new MarketPriceDay[MarketPrice.DaysShown];
        for (int i = 0; i < days.Length; i++)
        {
            long average = p.ReadLong();
            long max = p.ReadLong();
            long min = p.ReadLong();
            days[i] = new MarketPriceDay(average, max, min);
        }
        int trades = p.ReadInt();
        var lastUpdate = DateTimeOffset.FromUnixTimeSeconds(p.ReadUInt()).UtcDateTime;
        marketPrice.RaiseMarketPrice(new MarketPriceReply(result, itemId, days, trades, lastUpdate));
    }
}
