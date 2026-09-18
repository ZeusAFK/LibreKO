using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace LibreKO;

public partial class World : Node3D
{
    private void InventoryContext(int absSlot)
    {
        if (absSlot < 0 || absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;
        var def = ItemData.Get(Inv[absSlot].ItemId);
        if (def == null) return;

        if (ItemMove.RegionOf(absSlot) != ItemMove.Region.Grid
            || ItemData.EquipSlotFor(def) >= 0
            || def.Slot >= CospreCodeBase
            || def.Slot == ItemSlotCodeBag)
        {
            InventoryActivate(absSlot);
            return;
        }
        if (def.Effect1 != 0 && SkillData.IsSkill(def.Effect1))
            AddToHotbar(Inv[absSlot].ItemId);
    }

    private void InventoryActivate(int absSlot)
    {
        if (_moveInFlight || _moveQueue.Count > 0 || _selfDead) return;
        if (absSlot >= Inv.Length || Inv[absSlot].IsEmpty) return;
        var def = ItemData.Get(Inv[absSlot].ItemId);
        if (def == null) return;

        var region = ItemMove.RegionOf(absSlot);
        if (region != ItemMove.Region.Grid)
        {
            int free = Inv.FirstFreeGridSlot();
            if (free < 0) return;
            byte back = ItemMove.DirectionFor(region, ItemMove.Region.Grid);
            if (back == ItemMove.None) return;
            Enqueue(back, Inv[absSlot].ItemId, (byte)ItemMove.PositionIn(region, absSlot),
                    (byte)(free - GridStart), absSlot, free);
            return;
        }

        if (CospreDestinationFor(def) is { } cos)
        {
            Enqueue(ItemMove.InventoryToCospre, Inv[absSlot].ItemId,
                    (byte)(absSlot - GridStart), (byte)(cos - InventoryConstants.CospreStart), absSlot, cos);
            return;
        }

        if (def.Slot == ItemSlotCodeBag && FirstFreeBagSlot() is { } bagSlot)
        {
            Enqueue(ItemMove.InventoryToBagSlot, Inv[absSlot].ItemId,
                    (byte)(absSlot - GridStart),
                    (byte)(bagSlot - InventoryConstants.BagSlotStart), absSlot, bagSlot);
            return;
        }

        int eq = Inv.ResolveEquipDest(def.Slot, ItemData.EquipSlotFor(def));
        if (eq < 0) return;

        var preClear = Inv.HandsToClear(eq, def.Slot, IsTwoHanded);
        var free2 = Inv.FreeGridSlots();
        if (preClear.Count > free2.Count) return;
        for (int i = 0; i < preClear.Count; i++)
        {
            int handSlot = preClear[i], bag = free2[i];
            Enqueue(ItemMove.SlotToInventory, Inv[handSlot].ItemId, (byte)handSlot,
                    (byte)(bag - GridStart), handSlot, bag);
        }
        Enqueue(ItemMove.InventoryToSlot, Inv[absSlot].ItemId,
                (byte)(absSlot - GridStart), (byte)eq, absSlot, eq);
    }

    private const int ItemSlotCodeBag = 25;
    private const int CospreCodeBase = 100;

    private static bool IsVisualSlot(int abs)
        => abs < GridStart || InventoryConstants.IsCospreSlot(abs);

    private int? FirstFreeBagSlot()
    {
        for (int i = 0; i < InventoryConstants.BagSlotMax; i++)
        {
            int abs = InventoryConstants.BagSlotFor(i);
            if (abs < Inv.Length && Inv[abs].IsEmpty) return abs;
        }
        return null;
    }

    private int? CospreDestinationFor(ItemData.Item def)
    {
        if (def.Slot < CospreCodeBase) return null;
        int[] targets = (def.Slot % CospreCodeBase) switch
        {
            10 => new[] { InventoryConstants.CosPosWing },
            7 => new[] { InventoryConstants.CosPosHelmet },
            0 => new[] { InventoryConstants.CosPosGloveRight, InventoryConstants.CosPosGloveLeft },
            1 => new[] { InventoryConstants.CosPosGloveRight },
            2 => new[] { InventoryConstants.CosPosGloveLeft },
            5 => new[] { InventoryConstants.CosPosPauldron },
            14 => new[] { InventoryConstants.CosPosEmblem },
            11 => new[] { InventoryConstants.CosPosFairy },
            12 or 27 => new[] { InventoryConstants.CosPosTattoo },
            13 => new[] { InventoryConstants.CosPosTalisman },
            _ => System.Array.Empty<int>(),
        };
        if (targets.Length == 0) return null;
        foreach (int pos in targets)
        {
            int abs = InventoryConstants.CospreStart + pos;
            if (abs < Inv.Length && Inv[abs].IsEmpty) return abs;
        }
        return InventoryConstants.CospreStart + targets[0];
    }

    private static bool IsTwoHanded(int itemId) { var d = ItemData.Get(itemId); return d?.Slot is 3 or 4; }

    private void MoveBetween(int from, int to)
    {
        if (_moveInFlight || _moveQueue.Count > 0 || _selfDead) return;
        if (from == to || from < 0 || to < 0 || from >= Inv.Length || to >= Inv.Length) return;
        if (Inv[from].IsEmpty) return;

        var fromRegion = ItemMove.RegionOf(from);
        var toRegion = ItemMove.RegionOf(to);
        byte dir = ItemMove.DirectionFor(fromRegion, toRegion);
        if (dir == ItemMove.None) return;

        if (toRegion == ItemMove.Region.Equip)
        {
            var def = ItemData.Get(Inv[from].ItemId);
            if (def == null) return;
            var preClear = Inv.HandsToClear(to, def.Slot, IsTwoHanded);
            var free = Inv.FreeGridSlots();
            free.Remove(from);
            if (preClear.Count > free.Count) return;
            for (int i = 0; i < preClear.Count; i++)
            {
                int hand = preClear[i], bag = free[i];
                Enqueue(ItemMove.SlotToInventory, Inv[hand].ItemId, (byte)hand,
                        (byte)(bag - GridStart), hand, bag);
            }
        }

        Enqueue(dir, Inv[from].ItemId,
                (byte)ItemMove.PositionIn(fromRegion, from),
                (byte)ItemMove.PositionIn(toRegion, to),
                from, to);
    }

    private int CompareArrangeSlots(int a, int b)
    {
        var left = ItemData.Get(Inv[a].ItemId);
        var right = ItemData.Get(Inv[b].ItemId);
        int byKind = (left?.Kind ?? int.MaxValue).CompareTo(right?.Kind ?? int.MaxValue);
        if (byKind != 0) return byKind;
        int byItem = Inv[a].ItemId.CompareTo(Inv[b].ItemId);
        return byItem != 0 ? byItem : a.CompareTo(b);
    }

    private void Enqueue(byte dir, int itemId, byte src, byte dst, int from, int to)
    {
        _moveQueue.Enqueue(new MoveStep { Dir = dir, ItemId = itemId, Src = src, Dst = dst, From = from, To = to });
        PumpMoves();
    }

    private void PumpMoves()
    {
        if (_moveInFlight || _moveQueue.Count == 0) return;
        _moveCur = _moveQueue.Dequeue();
        _moveInFlight = true;
        Net.I.SendItemMove(_moveCur.Dir, _moveCur.ItemId, _moveCur.Src, _moveCur.Dst);
    }

    private void OnItemMoveResult(bool ok)
    {
        if (!_moveInFlight) return;
        _moveInFlight = false;
        if (!ok)
        {
            _moveQueue.Clear();
            RefreshInventoryUI();
            return;
        }

        Inv.Swap(_moveCur.From, _moveCur.To);
        if (IsVisualSlot(_moveCur.From) || IsVisualSlot(_moveCur.To)) RerenderSelfEquipment();
        AudioItemMove(_moveCur.ItemId, _moveCur.From, _moveCur.To);
        PumpMoves();
        RefreshInventoryUI();
    }

    private void OnItemGained(int itemId, int count)
    {
        Floaters?.Item(itemId, count);
        if (itemId == Net.GoldItemId) return;
        string name = ItemData.DisplayName(itemId);
        CombatLogAdd(count > 1 ? $"You obtained {name} x{count:n0}." : $"You obtained {name}.", CombatLogKind.Resource);
    }

    private void OnInventorySlotUpdate(int absSlot, ItemSlot item)
    {
        if (absSlot < 0) return;
        Inv.ApplySlotUpdate(absSlot, item);
        RefreshInventoryUI();
        if (IsVisualSlot(absSlot))
            RerenderSelfEquipment();
    }

    private void OnInventoryGridRefresh(ItemSlot[] items)
    {
        Inv.ApplyGridRefresh(items);
        RefreshInventoryUI();
    }

}
