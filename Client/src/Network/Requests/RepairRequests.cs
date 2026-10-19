namespace LibreKO.Network.Requests;

public interface IRepairRequests
{
    void Send(byte positionType, byte slot, int npcId, int itemId);
}

public sealed class RepairRequests : IRepairRequests
{
    private readonly IPacketSink sink;

    public RepairRequests(IPacketSink sink) => this.sink = sink;

    public void Send(byte positionType, byte slot, int npcId, int itemId)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_REPAIR);
        p.WriteByte(positionType);
        p.WriteByte(slot);
        p.WriteInt(npcId);
        p.WriteInt(itemId);
        sink.Send(p);
    }
}
