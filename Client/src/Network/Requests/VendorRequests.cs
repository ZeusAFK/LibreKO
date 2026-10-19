namespace LibreKO.Network.Requests;

public interface IVendorRequests
{
    void Repurchase(RepurchaseSubOpcode op);
    void RepurchaseBuy(byte row, int itemId, int npcId);
    void Buy(int sellingGroup, int npcId, int itemId, byte destPos, ushort count,
        byte line, byte listIndex);
    void Sell(int sellingGroup, int npcId, int itemId, byte srcPos, ushort count);
}

public sealed class VendorRequests : IVendorRequests
{
    private readonly IPacketSink sink;

    public VendorRequests(IPacketSink sink) => this.sink = sink;

    public void Repurchase(RepurchaseSubOpcode op)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte((byte)ItemTradeResult.Repurchase);
        p.WriteByte((byte)op);
        sink.Send(p);
    }

    public void RepurchaseBuy(byte row, int itemId, int npcId)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte((byte)ItemTradeResult.Repurchase);
        p.WriteByte((byte)RepurchaseSubOpcode.Buy);
        p.WriteByte(row);
        p.WriteInt(itemId);
        p.WriteInt(npcId);
        sink.Send(p);
    }

    public void Buy(int sellingGroup, int npcId, int itemId, byte destPos, ushort count,
            byte line, byte listIndex)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte(1);
        p.WriteInt(sellingGroup);
        p.WriteInt(npcId);
        p.WriteByte(1);
        p.WriteInt(itemId);
        p.WriteByte(destPos);
        p.WriteUShort(count);
        p.WriteByte(line);
        p.WriteByte(listIndex);
        sink.Send(p);
    }

    public void Sell(int sellingGroup, int npcId, int itemId, byte srcPos, ushort count)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_TRADE);
        p.WriteByte(2);
        p.WriteInt(sellingGroup);
        p.WriteInt(npcId);
        p.WriteByte(1);
        p.WriteInt(itemId);
        p.WriteByte(srcPos);
        p.WriteUShort(count);
        sink.Send(p);
    }
}
