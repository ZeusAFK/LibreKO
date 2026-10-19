namespace LibreKO.Network.Requests;

public interface IMerchantSearchRequests
{
    void Open(int itemId);
    void Results(int cursor);
    void Move(int sellerId);
}

public sealed class MerchantSearchRequests : IMerchantSearchRequests
{
    private readonly IPacketSink sink;

    public MerchantSearchRequests(IPacketSink sink) => this.sink = sink;

    public void Open(int itemId)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte((byte)MerchantSubOpcode.OfficialList);
        p.WriteByte((byte)MerchantSearchSubOpcode.Open);
        p.WriteInt(itemId);
        sink.Send(p);
    }

    public void Results(int cursor)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte((byte)MerchantSubOpcode.OfficialList);
        p.WriteByte((byte)MerchantSearchSubOpcode.Results);
        p.WriteInt(cursor);
        sink.Send(p);
    }

    public void Move(int sellerId)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT);
        p.WriteByte((byte)MerchantSubOpcode.OfficialList);
        p.WriteByte((byte)MerchantSearchSubOpcode.Move);
        p.WriteShort((short)sellerId);
        sink.Send(p);
    }
}
