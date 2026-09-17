namespace LibreKO.Game.World;

public class LootItem
{
    public int ItemId { get; set; }
    public ushort Count { get; set; }
}

public class LootBundle
{
    public const int MaxItems = 8;
    public const float MaxLootRange = 121.0f; // 11 units squared
    public const long OwnerExclusiveMs = 15_000; // 15 seconds owner-only looting

    private readonly Lock _sync = new();

    public int BundleId { get; set; }
    public List<LootItem> Items { get; } = [];

    public bool TryClaimSlot(int slotIndex, int expectedItemId, out LootItem? claimed)
    {
        claimed = null;
        using var scope = _sync.EnterScope();
        if (slotIndex < 0 || slotIndex >= Items.Count)
            return false;
        var item = Items[slotIndex];
        if (item.ItemId != expectedItemId)
            return false;
        Items.RemoveAt(slotIndex);
        claimed = item;
        return true;
    }

    public bool IsEmpty()
    {
        using var scope = _sync.EnterScope();
        return Items.Count == 0;
    }

    public List<LootItem> SnapshotItems()
    {
        using var scope = _sync.EnterScope();
        return [.. Items];
    }
    public float X { get; set; }
    public float Z { get; set; }
    public float Y { get; set; }
    public long DropTimeTicks { get; set; }

    // Owner tracking: only the owner (or their party) can loot during exclusive period
    public int OwnerCharId { get; set; }
    public int OwnerPartyIndex { get; set; } = -1;

    public bool IsExpired(long nowTicks, long lifetimeMs = 60_000)
        => nowTicks - DropTimeTicks > lifetimeMs * TimeSpan.TicksPerMillisecond;

    public bool IsOwnerExclusive(long nowTicks)
        => nowTicks - DropTimeTicks < OwnerExclusiveMs * TimeSpan.TicksPerMillisecond;

    public bool CanLoot(int charId, int partyIndex, long nowTicks)
    {
        if (!IsOwnerExclusive(nowTicks)) return true; // Exclusivity expired, anyone can loot
        if (OwnerCharId == charId) return true;
        if (OwnerPartyIndex >= 0 && OwnerPartyIndex == partyIndex) return true;
        return false;
    }

    public float DistanceSquaredTo(float x, float z)
    {
        float dx = X - x;
        float dz = Z - z;
        return dx * dx + dz * dz;
    }
}
