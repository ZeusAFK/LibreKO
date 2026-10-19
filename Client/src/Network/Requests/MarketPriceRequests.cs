namespace LibreKO.Network.Requests;

public interface IMarketPriceRequests
{
    void Send(int itemId);
}

public sealed class MarketPriceRequests : IMarketPriceRequests
{
    private readonly IPacketSink sink;

    public MarketPriceRequests(IPacketSink sink) => this.sink = sink;

    public void Send(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_MARKET_PRICE);
        p.WriteInt(itemId);
        sink.Send(p);
    }
}
