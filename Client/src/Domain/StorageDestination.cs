using System.Collections.Generic;

namespace LibreKO.Domain;

public static class StorageDestination
{
    public static bool Fits(ItemSlot target, int itemId, int count, bool stackable) => count > 0 &&
        (target.IsEmpty || stackable && target.ItemId == itemId && target.Count <= Inventory.StackMax - count);

    public static int Find(IReadOnlyList<ItemSlot> slots, int itemId, int count, bool stackable)
    {
        if (stackable)
            for (int i = 0; i < slots.Count; i++)
                if (!slots[i].IsEmpty && Fits(slots[i], itemId, count, true)) return i;
        for (int i = 0; i < slots.Count; i++) if (slots[i].IsEmpty) return i;
        return -1;
    }
}
