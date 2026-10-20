using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public sealed class InventoryJournal(UserSession session)
{
    private static readonly int[] Carried =
    [
        .. Enumerable.Range(InventoryConstants.InventoryStart, InventoryConstants.HaveMax),
        .. Enumerable.Range(InventoryConstants.MagicBagStart, InventoryConstants.MagicBagTotal),
    ];

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

    // One of an item leaves the first slot holding it, magic bags included, or nothing does.
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

    // What the change left is taken under the same lock as the change.
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

    // What the change made is taken back wherever it lies, and what it took goes back only once all it made is back.
    public void Restore()
    {
        var made = new List<(int Index, ItemSlot Part)>();
        var took = new List<(int Index, ItemSlot Part, bool Stacks)>();
        foreach (var (index, before) in slots)
        {
            if (written.TryGetValue(index, out var left))
                Split(index, before, left, made, took);
            else
                CopyInto(before, session.Inventory[index]);
        }

        var missing = made.Sum(entry => TakeBack(entry.Index, entry.Part));
        if (missing == 0)
        {
            foreach (var (index, part, stacks) in took)
                PutBack(index, part, stacks);
        }

        foreach (var (index, (entry, count)) in stall)
        {
            session.Trade.MerchantItems[index] = entry!;
            if (entry != null)
                entry.Count = count;
        }

        session.Money = (int)Math.Clamp(session.Money - moneyMoved, 0, ExchangePacketConstants.CoinMax);
    }

    private static void Split(int index, ItemSlot before, ItemSlot left,
        List<(int Index, ItemSlot Part)> made, List<(int Index, ItemSlot Part, bool Stacks)> took)
    {
        var stacks = before.Count > 1;
        if (!before.IsEmpty && !left.IsEmpty && !Alike(before, left))
        {
            made.Add((index, Copy(left)));
            took.Add((index, Copy(before), stacks));
            return;
        }

        var moved = CountOf(left) - CountOf(before);
        if (moved > 0)
            made.Add((index, Part(left, moved)));
        else if (moved < 0)
            took.Add((index, Part(before, -moved), stacks));
    }

    // The slot the change filled gives first, then the rest of the carried bag.
    private int TakeBack(int index, ItemSlot part)
    {
        var owed = (int)part.Count;
        foreach (var slot in Carried.Prepend(index).Distinct().Select(at => session.Inventory[at]).Where(slot => SameItem(slot, part)))
        {
            var taken = Math.Min(owed, slot.Count);
            slot.Count = (ushort)(slot.Count - taken);
            if (slot.Count == 0)
                slot.Clear();
            owed -= taken;
            if (owed == 0)
                break;
        }

        return owed;
    }

    // Only what the change cut from a stack goes back onto a stack; a single item needs an empty slot.
    private void PutBack(int index, ItemSlot part, bool stacks)
    {
        var item = new ExchangeItem
        {
            ItemId = part.ItemId, Count = part.Count, Durability = part.Durability, Flag = part.Flag,
            ExpiresAt = part.ExpiresAt, UniqueId = part.UniqueId, Countable = stacks,
        };
        var home = session.Inventory[index];
        var target = home.IsEmpty || ItemPlacement.StacksOnto(home, item)
            ? index
            : ItemPlacement.FindForReturn(session.Inventory, InventoryConstants.InventoryStart, InventoryConstants.HaveMax, item);
        if (target >= 0)
            ItemPlacement.Put(session.Inventory[target], item);
    }

    private static int CountOf(ItemSlot slot) => slot.IsEmpty ? 0 : slot.Count;

    private static ItemSlot Part(ItemSlot slot, int count)
    {
        var part = Copy(slot);
        part.Count = (ushort)count;
        return part;
    }

    private static bool SameItem(ItemSlot slot, ItemSlot other) =>
        !slot.IsEmpty && slot.ItemId == other.ItemId && slot.UniqueId == other.UniqueId
        && slot.Flag == other.Flag && slot.ExpiresAt == other.ExpiresAt;

    private static bool Alike(ItemSlot slot, ItemSlot other) => SameItem(slot, other) && slot.Durability == other.Durability;

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
