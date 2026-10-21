using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class ItemExpiry
{
    public static IReadOnlyList<int> Sweep(ItemSlot[] slots, long nowUnixSeconds, int start = 0, int count = int.MaxValue)
    {
        List<int>? cleared = null;
        var end = (int)Math.Min((long)start + count, slots.Length);
        for (var index = start; index < end; index++)
        {
            if (!slots[index].HasExpired(nowUnixSeconds))
                continue;
            slots[index].Clear();
            (cleared ??= []).Add(index);
        }
        return cleared ?? (IReadOnlyList<int>)[];
    }

    public static IReadOnlyList<int> RanOutWorn(ItemSlot[] inventory, long sinceUnixSeconds, long nowUnixSeconds)
    {
        List<int>? ranOut = null;
        for (var index = 0; index < InventoryConstants.MagicBagStart; index++)
        {
            if (index >= InventoryConstants.SlotMax && index < InventoryConstants.CospreStart)
                continue;

            var slot = inventory[index];
            if (slot.HasExpired(nowUnixSeconds) && !slot.HasExpired(sinceUnixSeconds))
                (ranOut ??= []).Add(slot.ItemId);
        }
        return ranOut ?? (IReadOnlyList<int>)[];
    }
}
