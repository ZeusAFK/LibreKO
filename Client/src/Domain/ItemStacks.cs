namespace LibreKO.Domain;

public static class ItemStacks
{
    public static bool IsStackable(int itemId) => ItemData.Get(itemId) is { Countable: not 0 };

    public static bool IsPartOfStack(int? amount, ItemSlot slot) => amount < slot.Count;

    public static int UseRefusal(ItemUse use, ItemSlot slot) =>
        ItemData.Get(slot.ItemId) is { } def
            ? ItemUseRules.Refusal(use, slot.ItemId, def.Race, slot.Flag, slot.ExpiresAt > 0)
            : ItemUseRules.Allowed;

    public static void MoveStack(ref ItemSlot from, ref ItemSlot to, int count, bool merge)
    {
        bool whole = from.Count <= count || !IsStackable(from.ItemId);
        if (merge) to.Count = (short)(to.Count + count);
        else
        {
            to = from;
            if (!whole) to.Count = (short)count;
        }
        if (whole) from = default;
        else from.Count = (short)(from.Count - count);
    }
}
