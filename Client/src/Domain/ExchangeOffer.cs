using System;
using System.Collections.Generic;
using System.Globalization;

namespace LibreKO.Domain;

public static class ExchangeOffer
{
    public const int ItemSlots = 12;

    public static bool IsOfferable(ItemSlot slot, ItemData.Item? def) =>
        slot.IsTradable && def != null && !ItemData.IsNoTradeId(slot.ItemId) && def.Race != ItemData.QuestItemRace;

    public static int SlotsUsed(IEnumerable<int> offeredItemIds, Func<int, bool> countable)
    {
        var stacked = new HashSet<int>();
        int used = 0;
        foreach (int itemId in offeredItemIds)
            if (!countable(itemId) || stacked.Add(itemId)) used++;
        return used;
    }

    public static bool HasRoomFor(IReadOnlyCollection<int> offeredItemIds, int itemId, Func<int, bool> countable)
    {
        if (countable(itemId))
            foreach (int offered in offeredItemIds)
                if (offered == itemId) return true;
        return SlotsUsed(offeredItemIds, countable) < ItemSlots;
    }

    public static bool TryParseAmount(string typed, int max, out int amount) =>
        int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out amount)
        && amount >= 1 && amount <= max;
}
