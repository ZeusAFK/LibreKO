namespace LibreKO.Network;

public readonly struct LootEntry
{
    public readonly int ItemId;
    public readonly int Count;

    public LootEntry(int itemId, int count) { ItemId = itemId; Count = count; }
}
