namespace LibreKO.Network.Readers;

internal sealed class LootReader : IPacketReader
{
    private readonly LootSession loot;

    public LootReader(LootSession loot)
    {
        this.loot = loot;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ITEM_DROP, HandleItemDrop);
        dispatcher.On(GameOpcodes.GS_BUNDLE_OPEN_REQ, HandleBundleOpen);
    }

    private void HandleItemDrop(Packet p)
    {
        if (p.RemainingBytes < 9) return;
        int sourceId = p.ReadInt();
        int bundleId = p.ReadInt();
        bool hasItems = p.ReadByte() != 0;
        if (hasItems) loot.RaiseLootDrop(sourceId, bundleId);
    }

    private void HandleBundleOpen(Packet p)
    {
        if (p.RemainingBytes < 5) return;
        int bundleId = p.ReadInt();
        int result = p.ReadByte();
        if (result != LootOpenResult.Opened)
        {
            loot.RaiseLootRefused(bundleId, result);
            return;
        }

        var entries = new List<LootEntry>(LootWire.MaxItems);
        for (int i = 0; i < LootWire.WireSlots && p.RemainingBytes >= 6; i++)
        {
            int itemId = p.ReadInt();
            int count = p.ReadUShort();
            if (itemId != 0) entries.Add(new LootEntry(itemId, count));
        }
        loot.RaiseLootContents(bundleId, entries);
    }
}
