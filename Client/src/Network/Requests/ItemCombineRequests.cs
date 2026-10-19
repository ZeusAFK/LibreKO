namespace LibreKO.Network.Requests;

public interface IItemCombineRequests
{
    void Send(int npcRuntimeId, int shadowItem, int shadowBagSlot, IReadOnlyList<CombineEntry> ordered);
}

public sealed class ItemCombineRequests : IItemCombineRequests
{
    private readonly IPacketSink sink;

    public ItemCombineRequests(IPacketSink sink) => this.sink = sink;

    public void Send(int npcRuntimeId, int shadowItem, int shadowBagSlot, IReadOnlyList<CombineEntry> ordered)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.Combine);
        p.WriteInt(npcRuntimeId);
        p.WriteInt(shadowItem);
        p.WriteByte((byte)shadowBagSlot);
        p.WriteByte((byte)ordered.Count);
        foreach (var entry in ordered)
            p.WriteByte((byte)entry.BagSlot);
        p.WriteSByteString(ItemCombine.MaterialText(ordered));
        sink.Send(p);
    }
}
