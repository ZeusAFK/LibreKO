namespace LibreKO.Session;

public sealed class MarketPriceSession
{
    public event Action<MarketPriceReply>? MarketPriceEvent;

    internal void RaiseMarketPrice(MarketPriceReply marketPriceReply) => MarketPriceEvent?.Invoke(marketPriceReply);
}
