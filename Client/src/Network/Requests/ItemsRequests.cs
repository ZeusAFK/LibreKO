namespace LibreKO.Network.Requests;

public interface IItemsRequests
{
    void Move(ItemMoveDirection direction, int itemId, byte sourcePos, byte destPos, ushort amount);
    void Remove(byte type, byte position, int itemId);
    void InventoryArrange();
    void BundleOpen(int bundleId);
    void ItemGet(int bundleId, int itemId, int bundleSlot);
    void ItemDrop(byte gridPos, int itemId, ushort count);
}

public sealed class ItemsRequests : IItemsRequests
{
    private readonly IPacketSink sink;
    private readonly ItemsSession items;

    public ItemsRequests(IPacketSink sink, ItemsSession items)
    {
        this.sink = sink;
        this.items = items;
    }

    public void InventoryArrange()
    {
        var p = new Packet(GameOpcodes.GS_ITEM_MOVE);
        p.WriteByte((byte)ItemMoveSubOpcode.ArrangeInventory);
        sink.Send(p);
    }

    public void BundleOpen(int bundleId)
    {
        var p = new Packet(GameOpcodes.GS_BUNDLE_OPEN_REQ);
        p.WriteInt(bundleId);
        sink.Send(p);
    }

    public void ItemGet(int bundleId, int itemId, int bundleSlot)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_GET);
        p.WriteInt(bundleId);
        p.WriteInt(itemId);
        p.WriteUShort((ushort)bundleSlot);
        sink.Send(p);
    }

    public void ItemDrop(byte gridPos, int itemId, ushort count)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_DROP);
        p.WriteByte(gridPos);
        p.WriteInt(itemId);
        p.WriteUShort(count);
        sink.Send(p);
    }

    public void Move(ItemMoveDirection direction, int itemId, byte sourcePos, byte destPos, ushort amount)
    {
        if (!sink.Connected) { items.RaiseItemMoveResult(false); return; }
        items.PendingMove = new PendingItemMove(direction, sourcePos, destPos, amount);
        sink.Send(ItemMoveWire.Move(direction, itemId, sourcePos, destPos, amount));
    }

    public void Remove(byte type, byte position, int itemId)
    {
        if (!sink.Connected) { items.RaiseItemRemoveResult(false, ItemRemoveReason.SystemError); return; }
        var p = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        p.WriteByte(type);
        p.WriteByte(position);
        p.WriteInt(itemId);
        items.PendingRemoveSlot = (ItemRemoveSource)type switch
        {
            ItemRemoveSource.Equipment => position,
            ItemRemoveSource.MagicBag => InventoryConstants.MagicBagStart + position,
            _ => InventoryConstants.InventoryStart + position,
        };
        sink.Send(p);
    }
}
