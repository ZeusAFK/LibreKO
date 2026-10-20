using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public sealed class InventoryJournal(UserSession session)
{
    private readonly Dictionary<int, ItemSlot> slots = [];
    private readonly Dictionary<int, (MerchantItem? Entry, ushort Count)> stall = [];
    private int? money;

    public IReadOnlyCollection<int> TouchedSlots => slots.Keys;

    public bool WasEmpty(int index) => slots.TryGetValue(index, out var before) && before.IsEmpty;

    public ItemSlot Slot(int index)
    {
        var slot = session.Inventory[index];
        slots.TryAdd(index, Copy(slot));
        return slot;
    }

    public MerchantItem? StallEntry(int index)
    {
        var entry = session.Trade.MerchantItems[index];
        stall.TryAdd(index, (entry, entry?.Count ?? 0));
        return entry;
    }

    public void ClearStallEntry(int index)
    {
        StallEntry(index);
        session.Trade.MerchantItems[index] = new MerchantItem();
    }

    // One of an item leaves the first bag slot holding it, or nothing does.
    public bool TakeOneFromBag(int itemId)
    {
        for (var index = InventoryConstants.InventoryStart; index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; index++)
        {
            if (session.Inventory[index].ItemId != itemId || session.Inventory[index].Count == 0)
                continue;

            var slot = Slot(index);
            slot.Count--;
            if (slot.Count == 0)
                slot.Clear();
            return true;
        }

        return false;
    }

    public void AddMoney(long amount)
    {
        money ??= session.Money;
        session.Money = checked((int)(session.Money + amount));
    }

    public void Restore()
    {
        foreach (var (index, before) in slots)
            CopyInto(before, session.Inventory[index]);
        foreach (var (index, (entry, count)) in stall)
        {
            session.Trade.MerchantItems[index] = entry!;
            if (entry != null)
                entry.Count = count;
        }

        if (money is { } previous)
            session.Money = previous;
    }

    public static void CopyInto(ItemSlot from, ItemSlot to)
    {
        to.ItemId = from.ItemId;
        to.Durability = from.Durability;
        to.Count = from.Count;
        to.Flag = from.Flag;
        to.ExpiresAt = from.ExpiresAt;
        to.UniqueId = from.UniqueId;
    }

    private static ItemSlot Copy(ItemSlot slot)
    {
        var copy = new ItemSlot();
        CopyInto(slot, copy);
        return copy;
    }
}
