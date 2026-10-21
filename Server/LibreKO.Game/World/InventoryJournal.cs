using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public sealed class InventoryJournal(UserSession session, ILogger? logger = null)
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

    // Whole once all it made is back; short of that, a change that took anything stands and one that took nothing goes back as far as it can.
    public void Restore()
    {
        var change = Split();
        var whole = IsWhole(change);
        if (whole || (change.Took.Count == 0 && moneyMoved >= 0))
            Undo(change, whole);
    }

    public static void RestoreTogether(params InventoryJournal[] journals)
    {
        if (journals.All(journal => journal.IsWhole(journal.Split())))
        {
            foreach (var journal in journals)
                journal.Restore();
        }
    }

    private sealed record Change(
        List<(int Index, ItemSlot Part)> Made,
        List<(int Index, ItemSlot Part, bool Stacks)> Took,
        List<(int Index, ItemSlot Before, int Wear)> Worn,
        List<int> Unsealed);

    private Change Split()
    {
        var change = new Change([], [], [], []);
        foreach (var (index, before) in slots)
        {
            if (!written.TryGetValue(index, out var left))
            {
                change.Unsealed.Add(index);
                continue;
            }

            var stacks = before.Count > 1;
            if (!before.IsEmpty && !left.IsEmpty && !SameItem(before, left))
            {
                change.Made.Add((index, Copy(left)));
                change.Took.Add((index, Copy(before), stacks));
                continue;
            }

            if (!before.IsEmpty && !left.IsEmpty && before.Durability != left.Durability)
                change.Worn.Add((index, Copy(before), left.Durability - before.Durability));
            var moved = CountOf(left) - CountOf(before);
            if (moved > 0)
                change.Made.Add((index, Part(left, moved)));
            else if (moved < 0)
                change.Took.Add((index, Part(before, -moved), stacks));
        }

        return change;
    }

    private bool IsWhole(Change change) =>
        (moneyMoved <= 0 || session.Money >= moneyMoved)
        && change.Made.GroupBy(entry => (entry.Part.ItemId, entry.Part.UniqueId, entry.Part.Flag, entry.Part.ExpiresAt))
            .All(kind => Holding(kind.Select(entry => entry.Index), kind.First().Part).Sum(slot => slot.Count) >= kind.Sum(entry => entry.Part.Count));

    private void Undo(Change change, bool whole)
    {
        foreach (var index in change.Unsealed)
            CopyInto(slots[index], session.Inventory[index]);
        foreach (var (index, part) in change.Made)
            TakeBack(index, part);
        foreach (var (index, before, wear) in change.Worn)
        {
            var slot = session.Inventory[index];
            if (SameItem(slot, before))
                slot.Durability = (short)(slot.Durability - wear);
        }

        if (whole)
        {
            foreach (var (index, part, stacks) in change.Took)
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

    private IEnumerable<ItemSlot> Holding(IEnumerable<int> first, ItemSlot kind) =>
        first.Concat(Carried).Distinct().Select(at => session.Inventory[at]).Where(slot => SameItem(slot, kind));

    private void TakeBack(int index, ItemSlot part)
    {
        var owed = (int)part.Count;
        foreach (var slot in Holding([index], part))
        {
            var taken = Math.Min(owed, slot.Count);
            slot.Count = (ushort)(slot.Count - taken);
            if (slot.Count == 0)
                slot.Clear();
            owed -= taken;
            if (owed == 0)
                break;
        }
    }

    // Only what the change cut from a stack goes back onto a stack, and what finds no room in the bag goes to the warehouse.
    private void PutBack(int index, ItemSlot part, bool stacks)
    {
        var item = new ExchangeItem
        {
            ItemId = part.ItemId, Count = part.Count, Durability = part.Durability, Flag = part.Flag,
            ExpiresAt = part.ExpiresAt, UniqueId = part.UniqueId, Countable = stacks,
        };
        var home = session.Inventory[index];
        if (home.IsEmpty || ItemPlacement.StacksOnto(home, item))
            ItemPlacement.Put(home, item);
        else if (!session.ReturnEscrowedItem(item))
            logger?.LogError("Undo for {Name} found no room in the bag or the warehouse for item {ItemId} x{Count}; it is lost",
                session.Name, item.ItemId, item.Count);
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
