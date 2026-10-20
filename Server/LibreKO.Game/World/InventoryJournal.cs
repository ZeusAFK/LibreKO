using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public sealed class InventoryJournal(UserSession session)
{
    private readonly Dictionary<int, ItemSlot> slots = [];
    private readonly Dictionary<int, ItemSlot> written = [];
    private readonly Dictionary<int, (MerchantItem? Entry, ushort Count)> stall = [];
    private long moneyMoved;

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
        session.Money = checked((int)(session.Money + amount));
        moneyMoved += amount;
    }

    // A restore looks for what the change left, so it is taken under the same lock as the change.
    public void Seal()
    {
        written.Clear();
        foreach (var index in slots.Keys)
            written[index] = Copy(session.Inventory[index]);
    }

    public T Apply<T>(Func<UserSession, T> change) => session.WithLock(active =>
    {
        var result = change(active);
        Seal();
        return result;
    });

    public void Apply(Action<UserSession> change) => Apply(active =>
    {
        change(active);
        return true;
    });

    // Gold and stacks go back by what the change moved; a slot changed since in any other way stays as the later change left it.
    public void Restore()
    {
        foreach (var (index, before) in slots)
        {
            var now = session.Inventory[index];
            if (!written.TryGetValue(index, out var left) || Same(now, left))
                CopyInto(before, now);
            else if (now.ItemId == left.ItemId && (before.IsEmpty || before.ItemId == left.ItemId))
                MoveCountBack(now, left.Count - (before.IsEmpty ? 0 : before.Count));
        }

        foreach (var (index, (entry, count)) in stall)
        {
            session.Trade.MerchantItems[index] = entry!;
            if (entry != null)
                entry.Count = count;
        }

        session.Money = (int)Math.Clamp(session.Money - moneyMoved, 0, ExchangePacketConstants.CoinMax);
    }

    private static void MoveCountBack(ItemSlot slot, int moved)
    {
        var count = Math.Min(slot.Count - moved, InventoryConstants.MaxStackCount);
        if (count > 0)
            slot.Count = (ushort)count;
        else
            slot.Clear();
    }

    private static bool Same(ItemSlot slot, ItemSlot other) =>
        slot.ItemId == other.ItemId && slot.Count == other.Count && slot.Durability == other.Durability
        && slot.Flag == other.Flag && slot.ExpiresAt == other.ExpiresAt && slot.UniqueId == other.UniqueId;

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
