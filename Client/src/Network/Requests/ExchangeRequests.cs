namespace LibreKO.Network.Requests;

public interface IExchangeRequests
{
    void ExchangeRequest(int targetCharId);
    void Agree(bool accept);
    void AddItem(byte gridPos, int itemId, int count);
    void AddGold(int goldAmount);
    void Decide();
    void Cancel();
}

public sealed class ExchangeRequests : IExchangeRequests
{
    private readonly IPacketSink sink;

    public ExchangeRequests(IPacketSink sink) => this.sink = sink;

    public void ExchangeRequest(int targetCharId)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(1);
        p.WriteInt(targetCharId);
        p.WriteByte(ExchangeModeTrade);
        sink.Send(p);
    }

    public void Agree(bool accept)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(2);
        p.WriteByte((byte)(accept ? 1 : 0));
        sink.Send(p);
    }

    public void AddItem(byte gridPos, int itemId, int count)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(3);
        p.WriteByte(gridPos);
        p.WriteInt(itemId);
        p.WriteInt(count);
        sink.Send(p);
    }

    public void AddGold(int goldAmount)
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(3);
        p.WriteByte(0xFF);
        p.WriteInt(ExchangeGoldItem);
        p.WriteInt(goldAmount);
        sink.Send(p);
    }

    public void Decide()
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(5);
        sink.Send(p);
    }

    public void Cancel()
    {
        var p = new Packet(GameOpcodes.GS_EXCHANGE);
        p.WriteByte(8);
        sink.Send(p);
    }

    public const int ExchangeGoldItem = 900000000;
    internal const byte ExchangeModeTrade = 1;
}
