using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class ItemPlacement
{
    public static bool IsSameItem(ItemSlot slot, ExchangeItem item) =>
        slot.ItemId == item.ItemId
        && slot.UniqueId == item.UniqueId
        && slot.Flag == item.Flag
        && slot.ExpiresAt == item.ExpiresAt;

    public static bool StacksOnto(ItemSlot slot, ExchangeItem item) =>
        item.Countable
        && !slot.IsEmpty
        && IsSameItem(slot, item)
        && slot.Count + item.Count <= InventoryConstants.MaxStackCount;

    public static int FindForReturn(ItemSlot[] slots, int start, int length, ExchangeItem item, int preferred = -1)
    {
        if (preferred >= start && preferred < start + length
            && (slots[preferred].IsEmpty || StacksOnto(slots[preferred], item)))
            return preferred;

        var empty = FirstEmpty(slots, start, length);
        return empty >= 0 ? empty : FirstStack(slots, start, length, item);
    }

    public static int FindForDelivery(ItemSlot[] slots, int start, int length, ExchangeItem item)
    {
        var stack = FirstStack(slots, start, length, item);
        return stack >= 0 ? stack : FirstEmpty(slots, start, length);
    }

    public static void Put(ItemSlot slot, ExchangeItem item)
    {
        if (!slot.IsEmpty)
        {
            slot.Count = (ushort)(slot.Count + item.Count);
            return;
        }

        slot.ItemId = item.ItemId;
        slot.Durability = item.Durability;
        slot.Count = (ushort)item.Count;
        slot.Flag = item.Flag;
        slot.ExpiresAt = item.ExpiresAt;
        slot.UniqueId = item.UniqueId;
    }

    private static int FirstEmpty(ItemSlot[] slots, int start, int length)
    {
        for (var i = start; i < start + length; i++)
        {
            if (slots[i].IsEmpty)
                return i;
        }

        return -1;
    }

    private static int FirstStack(ItemSlot[] slots, int start, int length, ExchangeItem item)
    {
        if (!item.Countable)
            return -1;

        for (var i = start; i < start + length; i++)
        {
            if (StacksOnto(slots[i], item))
                return i;
        }

        return -1;
    }
}
