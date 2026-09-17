using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public sealed class Inventory
{
    public const int GridStart = InventoryConstants.InventoryStart;

    public const int GridCount = InventoryConstants.HaveMax;

    private const int SlotOneHand = 0, SlotRightOnly = 1, SlotLeftOnly = 2;
    private const int SlotTwoHandRight = 3, SlotTwoHandLeft = 4;
    private const int SlotEar = 10, SlotRing = 12;

    private ItemSlot[] _slots = Array.Empty<ItemSlot>();

    public int Length => _slots.Length;

    public ItemSlot this[int abs]
    {
        get => _slots[abs];
        set => _slots[abs] = value;
    }

    public void Reset(ItemSlot[]? seed) =>
        _slots = seed is null ? Array.Empty<ItemSlot>() : (ItemSlot[])seed.Clone();

    public void EnsureLength(int length)
    {
        if (_slots.Length >= length) return;
        Array.Resize(ref _slots, length);
    }

    public bool IsGridSlot(int abs) => abs >= GridStart;

    public bool IsEquipSlot(int abs) => abs >= 0 && abs < GridStart;

    public int FirstFreeGridSlot()
    {
        for (int abs = GridStart; abs < GridStart + GridCount && abs < _slots.Length; abs++)
            if (_slots[abs].IsEmpty) return abs;
        return -1;
    }

    public int CountOf(int itemId)
    {
        int total = 0;
        for (int abs = GridStart; abs < GridStart + GridCount && abs < _slots.Length; abs++)
            if (_slots[abs].ItemId == itemId) total += _slots[abs].Count;
        return total;
    }

    public List<int> FreeGridSlots()
    {
        var free = new List<int>();
        for (int abs = GridStart; abs < GridStart + GridCount && abs < _slots.Length; abs++)
            if (_slots[abs].IsEmpty) free.Add(abs);
        return free;
    }

    public void Swap(int a, int b) => (_slots[a], _slots[b]) = (_slots[b], _slots[a]);

    public const int StackMax = 9999;

    public void Stack(int abs, int amount)
    {
        var slot = _slots[abs];
        slot.Count = (short)Math.Min(StackMax, slot.Count + amount);
        _slots[abs] = slot;
    }

    public void Consume(int abs, int amount)
    {
        var slot = _slots[abs];
        slot.Count -= (short)amount;
        if (slot.Count <= 0) slot.Clear();
        _slots[abs] = slot;
    }

    public void SetDurability(int abs, short durability)
    {
        var slot = _slots[abs];
        slot.Durability = durability;
        _slots[abs] = slot;
    }

    public void ApplySlotUpdate(int abs, ItemSlot item)
    {
        if (abs < 0) return;
        EnsureLength(abs + 1);
        if (item.ItemId == 0 || item.Count == 0)
            item = default;
        else if (item.Durability == 0 && _slots[abs].ItemId == item.ItemId && _slots[abs].Durability != 0)
            item.Durability = _slots[abs].Durability;
        _slots[abs] = item;
    }

    public void ApplyGridRefresh(ItemSlot[] items)
    {
        EnsureLength(GridStart + GridCount);
        for (int i = 0; i < GridCount && i < items.Length; i++)
            _slots[GridStart + i] = items[i];
    }

    public static bool IsTwoHandedSlotType(int slotType) =>
        slotType is SlotTwoHandRight or SlotTwoHandLeft;

    public int ResolveEquipDest(int slotType, int fallback) => slotType switch
    {
        SlotOneHand => Prefer(InventoryConstants.RightHand, InventoryConstants.LeftHand),
        SlotRightOnly or SlotTwoHandRight => InventoryConstants.RightHand,
        SlotLeftOnly or SlotTwoHandLeft => InventoryConstants.LeftHand,
        SlotEar => Prefer(InventoryConstants.RightEar, InventoryConstants.LeftEar),
        SlotRing => Prefer(InventoryConstants.RightRing, InventoryConstants.LeftRing),
        _ => fallback,
    };

    private int Prefer(int first, int second) =>
        _slots[first].IsEmpty ? first : (_slots[second].IsEmpty ? second : first);

    public List<int> HandsToClear(int dest, int slotType, Func<int, bool> isTwoHandedItem)
    {
        var clear = new List<int>();
        if (dest is not (InventoryConstants.RightHand or InventoryConstants.LeftHand)) return clear;

        int other = dest == InventoryConstants.RightHand
            ? InventoryConstants.LeftHand
            : InventoryConstants.RightHand;
        if (_slots[other].IsEmpty) return clear;

        if (IsTwoHandedSlotType(slotType) || isTwoHandedItem(_slots[other].ItemId))
            clear.Add(other);
        return clear;
    }
}
