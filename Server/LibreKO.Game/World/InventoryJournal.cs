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

    // One of an item leaves the first slot holding it, magic bags included as the item checks count them, or nothing does.
    public bool TakeOne(int itemId)
    {
        for (var index = InventoryConstants.InventoryStart; index < session.Inventory.Length; index++)
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

    // The whole count reaches the bag or none of it does: a countable item as one stack, any other one per slot.
    public bool DeliverAll(ItemData item, int count)
    {
        var countable = item.Countable != 0;
        var delivery = new ExchangeItem
        {
            ItemId = item.Num, Count = countable ? count : 1, Durability = item.Duration, Countable = countable,
        };
        if (countable)
        {
            var index = count <= InventoryConstants.MaxStackCount
                ? ItemPlacement.FindForDelivery(session.Inventory, InventoryConstants.InventoryStart, InventoryConstants.HaveMax, delivery)
                : -1;
            if (index < 0)
                return false;

            ItemPlacement.Put(Slot(index), delivery);
            return true;
        }

        var empty = Enumerable.Range(InventoryConstants.InventoryStart, InventoryConstants.HaveMax)
            .Where(index => session.Inventory[index].IsEmpty)
            .Take(count)
            .ToList();
        if (empty.Count < count)
            return false;

        foreach (var index in empty)
            ItemPlacement.Put(Slot(index), delivery);
        return true;
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
