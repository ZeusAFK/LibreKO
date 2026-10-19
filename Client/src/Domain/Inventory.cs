using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class Inventory
{
    public const int GridStart = InventoryConstants.InventoryStart;

    public const int GridCount = InventoryConstants.HaveMax;

    private ItemSlot[] slots = Array.Empty<ItemSlot>();

    public int Length => slots.Length;

    public ItemSlot this[int abs]
    {
        get => slots[abs];
        set => slots[abs] = value;
    }

    public IReadOnlyList<ItemSlot> Slots => slots;

    public void Reset(ItemSlot[]? seed) =>
        slots = seed is null ? Array.Empty<ItemSlot>() : (ItemSlot[])seed.Clone();

    public void EnsureLength(int length)
    {
        if (slots.Length >= length) return;
        Array.Resize(ref slots, length);
    }

    public int ItemIdAt(int abs) => abs >= 0 && abs < slots.Length ? slots[abs].ItemId : 0;

    public int HandWeapon(bool rightFirst)
    {
        int right = ItemIdAt(InventoryConstants.RightHand), left = ItemIdAt(InventoryConstants.LeftHand);
        return rightFirst ? (right != 0 ? right : left) : (left != 0 ? left : right);
    }

    public bool IsGridSlot(int abs) => abs >= GridStart;

    public bool IsEquipSlot(int abs) => abs >= 0 && abs < GridStart;

    public bool Holds(int abs, int itemId) =>
        itemId != 0 && abs >= 0 && abs < slots.Length && slots[abs].ItemId == itemId;

    public bool Holds(int abs, ItemSlot item) =>
        !item.IsEmpty && abs >= 0 && abs < slots.Length && slots[abs].Equals(item);

    public int FirstFreeGridSlot()
    {
        for (int abs = GridStart; abs < GridStart + GridCount && abs < slots.Length; abs++)
            if (slots[abs].IsEmpty) return abs;
        return -1;
    }

    public int GridSlotFor(int itemId, int count, bool stackable)
    {
        if (stackable)
            for (int abs = GridStart; abs < GridStart + GridCount && abs < slots.Length; abs++)
                if (slots[abs].ItemId == itemId && slots[abs].Count + count <= StackMax) return abs;
        return FirstFreeGridSlot();
    }

    public int GridRoomFor(int itemId, bool stackable)
    {
        int room = FirstFreeGridSlot() >= 0 ? (stackable ? StackMax : 1) : 0;
        if (!stackable) return room;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < slots.Length; abs++)
            if (slots[abs].ItemId == itemId) room = Math.Max(room, StackMax - slots[abs].Count);
        return room;
    }

    public int FirstStackOrFreeGridSlot(ItemSlot source, int countable, Func<int, bool> staged)
    {
        for (int abs = GridStart; abs < GridStart + GridCount && abs < slots.Length; abs++)
            if (!staged(abs) && ItemMove.Merges(ItemMoveDirection.MagicBagToInventory, source, slots[abs], countable)) return abs;
        return FirstFreeGridSlot();
    }

    public int CountOf(int itemId)
    {
        int total = 0;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < slots.Length; abs++)
            if (slots[abs].ItemId == itemId) total += slots[abs].Count;
        return total;
    }

    public List<int> FreeGridSlots()
    {
        var free = new List<int>();
        for (int abs = GridStart; abs < GridStart + GridCount && abs < slots.Length; abs++)
            if (slots[abs].IsEmpty) free.Add(abs);
        return free;
    }

    public void Swap(int a, int b) => (slots[a], slots[b]) = (slots[b], slots[a]);

    public const int StackMax = 9999;

    public static ItemSlot Stacked(ItemSlot slot, int amount)
    {
        slot.Count = (short)Math.Min(StackMax, slot.Count + amount);
        return slot;
    }

    public static ItemSlot Consumed(ItemSlot slot, int amount)
    {
        slot.Count -= (short)amount;
        if (slot.Count <= 0) slot.Clear();
        return slot;
    }

    public void Stack(int abs, int amount) => slots[abs] = Stacked(slots[abs], amount);

    public void Consume(int abs, int amount) => slots[abs] = Consumed(slots[abs], amount);

    public void Split(int from, int to, int amount)
    {
        if (slots[to].IsEmpty)
        {
            var part = slots[from];
            part.Count = (short)amount;
            slots[to] = part;
        }
        else
            Stack(to, amount);
        Consume(from, amount);
    }

    public void SetDurability(int abs, short durability)
    {
        var slot = slots[abs];
        slot.Durability = durability;
        slots[abs] = slot;
    }

    public void Put(int abs, ItemSlot item)
    {
        if (abs < 0) return;
        EnsureLength(abs + 1);
        slots[abs] = item.ItemId == 0 || item.Count == 0 ? default : item;
    }

    public void ApplyMove(ItemMoveDirection direction, int from, int to, ushort amount)
    {
        EnsureLength(Math.Max(from, to) + 1);
        ItemMove.ApplyConfirmed(slots, direction, from, to, ItemData.Get(slots[from].ItemId)?.Countable ?? 0, amount);
    }

    public void ApplySlotUpdate(int abs, ItemSlot item)
    {
        if (abs < 0) return;
        EnsureLength(abs + 1);
        if (item.ItemId == 0 || item.Count == 0)
            item = default;
        else if (item.Durability == 0 && slots[abs].ItemId == item.ItemId && slots[abs].Durability != 0)
            item.Durability = slots[abs].Durability;
        slots[abs] = item;
    }

    public void ApplyGridRefresh(ItemSlot[] items)
    {
        EnsureLength(GridStart + GridCount);
        for (int i = 0; i < GridCount && i < items.Length; i++)
            slots[GridStart + i] = items[i];
    }

    public static bool IsTwoHandedSlotType(int slotType) =>
        slotType is ItemData.SlotType.TwoHandRight or ItemData.SlotType.TwoHandLeft;

    public int ResolveEquipDest(int slotType, int fallback) => slotType switch
    {
        ItemData.SlotType.OneHand => Prefer(InventoryConstants.RightHand, InventoryConstants.LeftHand),
        ItemData.SlotType.RightOnly or ItemData.SlotType.TwoHandRight => InventoryConstants.RightHand,
        ItemData.SlotType.LeftOnly or ItemData.SlotType.TwoHandLeft => InventoryConstants.LeftHand,
        ItemData.SlotType.Earring => Prefer(InventoryConstants.RightEar, InventoryConstants.LeftEar),
        ItemData.SlotType.Ring => Prefer(InventoryConstants.RightRing, InventoryConstants.LeftRing),
        _ => fallback,
    };

    private int Prefer(int first, int second) =>
        slots[first].IsEmpty ? first : (slots[second].IsEmpty ? second : first);

    public List<int> HandsToClear(int dest, int slotType, Func<int, bool> isTwoHandedItem)
    {
        var clear = new List<int>();
        if (dest is not (InventoryConstants.RightHand or InventoryConstants.LeftHand)) return clear;

        int other = dest == InventoryConstants.RightHand
            ? InventoryConstants.LeftHand
            : InventoryConstants.RightHand;
        if (slots[other].IsEmpty) return clear;

        if (IsTwoHandedSlotType(slotType) || isTwoHandedItem(slots[other].ItemId))
            clear.Add(other);
        return clear;
    }

    public int CountInBackpack(int itemId)
    {
        int n = 0;
        for (int abs = GridStart; abs < Length; abs++)
            if (this[abs].ItemId == itemId) n += Math.Max(ItemData.ShownCount(ItemData.Get(itemId), this[abs]), 1);
        return n;
    }

    public bool InMainBag(int abs) => abs >= GridStart && abs < GridStart + GridCount && abs < Length;
}
