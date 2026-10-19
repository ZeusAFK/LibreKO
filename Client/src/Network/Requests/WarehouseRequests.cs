namespace LibreKO.Network.Requests;

public interface IWarehouseRequests
{
    void Open();
    void MoveInside(int npcId, int itemId, byte page, byte srcPos, byte dstPage, byte dstPos);
    void Input(int npcId, int itemId, byte page, byte srcInvPos, byte dstWhPos, int count);
    void Output(int npcId, int itemId, byte page, byte srcWhPos, byte dstInvPos, int count);
}

public sealed class WarehouseRequests : IWarehouseRequests
{
    private readonly IPacketSink sink;

    public WarehouseRequests(IPacketSink sink) => this.sink = sink;

    public void Open()
    {
        var p = new Packet(GameOpcodes.GS_WAREHOUSE);
        p.WriteByte(1);
        sink.Send(p);
    }

    public void MoveInside(int npcId, int itemId, byte page, byte srcPos, byte dstPage, byte dstPos)
    {
        var p = new Packet(GameOpcodes.GS_WAREHOUSE);
        p.WriteByte((byte)WarehouseSubOpcode.Move);
        p.WriteInt(npcId);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteByte(dstPage);
        sink.Send(p);
    }

    public void Input(int npcId, int itemId, byte page, byte srcInvPos, byte dstWhPos, int count)
            => SendWarehouseMove(2, npcId, itemId, page, srcInvPos, dstWhPos, count);

    public void Output(int npcId, int itemId, byte page, byte srcWhPos, byte dstInvPos, int count)
            => SendWarehouseMove(3, npcId, itemId, page, srcWhPos, dstInvPos, count);

    private void SendWarehouseMove(byte sub, int npcId, int itemId, byte page, byte srcPos, byte dstPos, int count)
    {
        var p = new Packet(GameOpcodes.GS_WAREHOUSE);
        p.WriteByte(sub);
        p.WriteInt(npcId);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteInt(count);
        sink.Send(p);
    }
}
