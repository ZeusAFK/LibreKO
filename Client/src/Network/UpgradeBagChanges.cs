using System;
using System.Collections.Generic;
using LibreKO.Domain;

namespace LibreKO.Network;

public static class UpgradeBagChanges
{
    private const byte RequestNormal = 1;
    private const byte ResultFailed = 0;
    private const byte ResultSucceeded = 1;

    public static List<(int Abs, ItemSlot Slot)> For(byte upgradeType, byte result,
        IReadOnlyList<UpgradeSlotResult> slots, IReadOnlyList<ItemSlot> inventory, Func<int, short> durability)
    {
        var changes = new List<(int Abs, ItemSlot Slot)>();
        if (upgradeType != RequestNormal || result is not (ResultSucceeded or ResultFailed) || slots.Count == 0)
            return changes;

        var working = new Dictionary<int, ItemSlot>();
        var origin = slots[0];
        if (origin.Position >= 0 && origin.Position < InventoryConstants.HaveMax)
        {
            int abs = InventoryConstants.InventoryStart + origin.Position;
            if (result == ResultSucceeded && origin.ItemId != 0)
                working[abs] = new ItemSlot { ItemId = origin.ItemId, Count = 1, Durability = durability(origin.ItemId) };
            else if (result == ResultFailed)
                working[abs] = default;
            if (working.TryGetValue(abs, out var changed)) changes.Add((abs, changed));
        }

        for (int i = 1; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot.Position < 0 || slot.Position >= InventoryConstants.HaveMax) continue;
            int abs = InventoryConstants.InventoryStart + slot.Position;
            var current = working.TryGetValue(abs, out var seen) ? seen : abs < inventory.Count ? inventory[abs] : default;
            if (current.ItemId == 0 || current.ItemId != slot.ItemId) continue;
            if (current.Count > 1) current.Count--;
            else current = default;
            working[abs] = current;
            changes.Add((abs, current));
        }
        return changes;
    }
}
