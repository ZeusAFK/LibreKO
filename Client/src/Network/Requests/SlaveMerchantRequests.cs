namespace LibreKO.Network.Requests;

public interface ISlaveMerchantRequests
{
    void Status();
    void Collect();
    void Redeem(byte bagPosition, int itemId);
}

public sealed class SlaveMerchantRequests : ISlaveMerchantRequests
{
    private readonly IPacketSink sink;

    public SlaveMerchantRequests(IPacketSink sink) => this.sink = sink;

    public void Status() => sink.Send(Request(SlaveMerchantSub.Status));

    public void Collect() => sink.Send(Request(SlaveMerchantSub.Collect));

    public void Redeem(byte bagPosition, int itemId)
    {
        var p = Request(SlaveMerchantSub.Redeem);
        p.WriteByte(bagPosition);
        p.WriteInt(itemId);
        sink.Send(p);
    }

    private static Packet Request(SlaveMerchantSub sub)
    {
        var p = new Packet(GameOpcodes.GS_SLAVE_MERCHANT);
        p.WriteByte((byte)sub);
        return p;
    }
}
