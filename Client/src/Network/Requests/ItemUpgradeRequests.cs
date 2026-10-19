namespace LibreKO.Network.Requests;

public interface IItemUpgradeRequests
{
    void ItemSeal(ItemSealType sealType, int itemId, byte srcPos, string code = "");
    void ItemBind(int itemId, byte srcPos);
    void UpgradeRequest(int npcId, int[] itemIds, int[] positions, bool preview = false);
}

public sealed class ItemUpgradeRequests : IItemUpgradeRequests
{
    private readonly IPacketSink sink;

    public ItemUpgradeRequests(IPacketSink sink) => this.sink = sink;

    public void ItemSeal(ItemSealType sealType, int itemId, byte srcPos, string code = "")
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.ItemSeal);
        p.WriteByte((byte)sealType);
        p.WriteInt(SealRequestNoWindow);
        p.WriteInt(itemId);
        p.WriteByte(srcPos);
        p.WriteString(code);
        sink.Send(p);
    }

    public void ItemBind(int itemId, byte srcPos)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)ItemUpgradeSubOpcode.ItemSeal);
        p.WriteByte((byte)ItemSealType.Bind);
        p.WriteInt(BindRequestFromBag);
        p.WriteInt(itemId);
        p.WriteByte(srcPos);
        p.WriteString(BindRequestCode);
        sink.Send(p);
    }

    public void UpgradeRequest(int npcId, int[] itemIds, int[] positions, bool preview = false)
    {
        SendUpgrade(
            SubOpcodeFor(itemIds),
            preview ? ItemUpgradeKind.Preview : ItemUpgradeKind.Normal,
            npcId,
            itemIds,
            positions);
    }

    internal const int SealRequestNoWindow = -1;
    internal const int BindRequestFromBag = 0;
    internal const string BindRequestCode = "1";

    private static ItemUpgradeSubOpcode SubOpcodeFor(int[] itemIds)
    {
        for (int i = 1; i < itemIds.Length; i++)
        {
            if (itemIds[i] >= UpgradeScrolls.AccessoryCompoundFirst && itemIds[i] <= UpgradeScrolls.AccessoryCompoundLast)
                return ItemUpgradeSubOpcode.UpgradeAccessories;
            if (itemIds[i] == UpgradeScrolls.Reverse)
                return ItemUpgradeSubOpcode.UpgradeRebirth;
        }
        return ItemUpgradeSubOpcode.Upgrade;
    }

    private void SendUpgrade(ItemUpgradeSubOpcode subOpcode, ItemUpgradeKind upgradeType, int npcId, int[] itemIds, int[] positions)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte((byte)subOpcode);
        p.WriteByte((byte)upgradeType);
        p.WriteInt(npcId);
        for (int i = 0; i < ItemUpgradeWire.SlotCount; i++)
        {
            p.WriteInt(i < itemIds.Length ? itemIds[i] : 0);
            int pos = i < positions.Length ? positions[i] : -1;
            p.WriteByte(pos >= 0 ? (byte)pos : byte.MaxValue);
        }
        sink.Send(p);
    }
}
