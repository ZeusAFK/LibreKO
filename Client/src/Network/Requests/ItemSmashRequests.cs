namespace LibreKO.Network.Requests;

public interface IItemSmashRequests
{
    void Send(int itemId, int bagSlot, int npcRuntimeId);
}

public sealed class ItemSmashRequests : IItemSmashRequests
{
    private readonly IPacketSink sink;

    public ItemSmashRequests(IPacketSink sink) => this.sink = sink;

    public void Send(int itemId, int bagSlot, int npcRuntimeId)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.Smash);
        p.WriteInt(itemId);
        p.WriteByte((byte)bagSlot);
        p.WriteInt(npcRuntimeId);
        sink.Send(p);
    }
}
