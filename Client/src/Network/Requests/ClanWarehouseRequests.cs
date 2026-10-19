namespace LibreKO.Network.Requests;

public interface IClanWarehouseRequests
{
    void ClanWhOpen();
    void ClanWhMoveInside(int itemId, byte page, byte srcPos, byte dstPage, byte dstPos);
    void ClanWhInput(int itemId, byte page, byte srcInvPos, byte dstWhPos, int count);
    void ClanWhOutput(int itemId, byte page, byte srcWhPos, byte dstInvPos, int count);
}

public sealed class ClanWarehouseRequests : IClanWarehouseRequests
{
    private readonly IPacketSink sink;

    public ClanWarehouseRequests(IPacketSink sink) => this.sink = sink;

    public void ClanWhOpen()
    {
        var p = new Packet(GameOpcodes.GS_CLAN_WAREHOUSE);
        p.WriteByte((byte)WarehouseSubOpcode.Open);
        sink.Send(p);
    }

    public void ClanWhMoveInside(int itemId, byte page, byte srcPos, byte dstPage, byte dstPos)
    {
        var p = new Packet(GameOpcodes.GS_CLAN_WAREHOUSE);
        p.WriteByte((byte)WarehouseSubOpcode.Move);
        p.WriteInt(0);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteByte(dstPage);
        sink.Send(p);
    }

    public void ClanWhInput(int itemId, byte page, byte srcInvPos, byte dstWhPos, int count)
            => SendClanWhMove(WarehouseSubOpcode.Input, itemId, page, srcInvPos, dstWhPos, count);

    public void ClanWhOutput(int itemId, byte page, byte srcWhPos, byte dstInvPos, int count)
            => SendClanWhMove(WarehouseSubOpcode.Output, itemId, page, srcWhPos, dstInvPos, count);

    private void SendClanWhMove(WarehouseSubOpcode sub, int itemId, byte page, byte srcPos, byte dstPos, int count)
    {
        var p = new Packet(GameOpcodes.GS_CLAN_WAREHOUSE);
        p.WriteByte((byte)sub);
        p.WriteInt(0);
        p.WriteInt(itemId);
        p.WriteByte(page);
        p.WriteByte(srcPos);
        p.WriteByte(dstPos);
        p.WriteInt(count);
        sink.Send(p);
    }
}
