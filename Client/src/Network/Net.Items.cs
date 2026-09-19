using System;

namespace LibreKO.Network;

public partial class Net
{
    private readonly struct PendingItemMove
    {
        public readonly byte Dir, Src, Dst;
        public PendingItemMove(byte dir, byte src, byte dst) { Dir = dir; Src = src; Dst = dst; }
    }
    private PendingItemMove? _pendingItemMove;
    private int _pendingRemoveSlot = -1;

    private void HandleItemMove(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte command = p.ReadByte();
        if (command == 1)
        {
            if (p.RemainingBytes < 1) return;
            bool ok = p.ReadByte() == 1;
            if (ok) ApplyPendingItemMove();
            else _pendingItemMove = null;
            ItemMoveResultEvent?.Invoke(ok);
            if (ok && TryReadItemStats(p, out var stats))
            {
                ApplyLastStats(stats);
                ItemStatsEvent?.Invoke(stats);
            }
        }
        else if (command == 2)
        {
            if (p.RemainingBytes < 1) return;
            bool ok = p.ReadByte() == 1;
            if (!ok) return;
            if (p.RemainingBytes < InventoryConstants.HaveMax * 19) return;
            var items = new ItemSlot[InventoryConstants.HaveMax];
            for (int i = 0; i < items.Length; i++)
            {
                int itemId = p.ReadInt();
                short dur = UShortToShort(p.ReadUShort());
                short count = UShortToShort(p.ReadUShort());
                byte flag = p.ReadByte(); p.ReadUShort(); p.ReadInt(); p.ReadInt();
                items[i] = new ItemSlot
                {
                    ItemId = itemId, Durability = dur, Count = count, Flag = flag,
                };
            }
            ApplyLastInventoryGridRefresh(items);
            InventoryGridRefreshEvent?.Invoke(items);
        }
    }

    private const int ItemCountChangeEntryBytes = 17;

    private void HandleItemCountChange(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int entries = p.ReadShort();
        for (int i = 0; i < entries; i++)
        {
            if (p.RemainingBytes < ItemCountChangeEntryBytes) return;
            byte kind = p.ReadByte();
            byte pos = p.ReadByte();
            int itemId = p.ReadInt();
            short count = IntToShort(p.ReadInt());
            p.ReadByte();
            short dur = p.ReadShort();
            p.ReadInt();
            if (kind == 0 || pos >= InventoryConstants.HaveMax) continue;

            int abs = InventoryConstants.InventoryStart + pos;
            var item = new ItemSlot { ItemId = itemId, Durability = dur, Count = count };
            SetLastInventorySlot(abs, item);
            InventorySlotEvent?.Invoke(abs, item);
            try
            {
                RaiseItemGained(abs, itemId, count);
            }
            catch (Exception ex)
            {
                Diag.Report("ItemGainedEvent", ex);
            }
        }
    }

    private void HandleItemGet(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte result = p.ReadByte();
        if (result != 1)
        {
            LootFailEvent?.Invoke(result);
            return;
        }
        if (p.RemainingBytes < 17) return;
        int bundleId = p.ReadInt();
        byte pos = p.ReadByte();
        int itemId = p.ReadInt();
        short count = UShortToShort(p.ReadUShort());
        int money = p.ReadInt();
        int bundleSlot = p.ReadUShort();
        if (pos == 0xFF)
            GoldChangeEvent?.Invoke(money);
        else if (pos < InventoryConstants.HaveMax)
        {
            int abs = InventoryConstants.InventoryStart + pos;
            var item = new ItemSlot { ItemId = itemId, Count = count };
            PreserveLastDurability(abs, ref item);
            SetLastInventorySlot(abs, item);
            InventorySlotEvent?.Invoke(abs, item);
            try
            {
                RaiseItemGained(abs, itemId, count);
            }
            catch (Exception ex)
            {
                Diag.Report("ItemGainedEvent", ex);
            }
        }
        LootTakenEvent?.Invoke(bundleId, bundleSlot, itemId);
    }

    private void HandleItemRemove(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        bool ok = p.ReadByte() == 1;
        if (!ok && p.RemainingBytes >= 1) p.ReadByte();
        if (ok && _pendingRemoveSlot >= 0)
        {
            int slot = _pendingRemoveSlot;
            SetLastInventorySlot(slot, default);
            InventorySlotEvent?.Invoke(slot, default);
        }
        _pendingRemoveSlot = -1;
        ItemRemoveResultEvent?.Invoke(ok);
    }

    private void HandleUserLookChange(Packet p)
    {
        if (p.RemainingBytes < 9) return;
        int charId = p.ReadInt();
        int slot = p.ReadByte();
        int itemId = p.ReadInt();
        short durability = p.RemainingBytes >= 2 ? UShortToShort(p.ReadUShort()) : (short)0;
        if (p.RemainingBytes >= 1) p.ReadByte();
        if (_known.TryGetValue(charId, out var e) && !e.IsNpc)
        {
            int gearIndex = VisualGearIndexForLookSlot(slot);
            if (gearIndex >= 0)
            {
                if (e.Gear.Length < InventoryConstants.VisualSlotCount)
                    System.Array.Resize(ref e.Gear, InventoryConstants.VisualSlotCount);
                e.Gear[gearIndex] = itemId;
            }
        }
        LookChangeEvent?.Invoke(charId, slot, itemId, durability);
    }

    private void HandlePointChange(Packet p)
    {
        if (p.RemainingBytes < 13) return;
        int type = p.ReadByte();
        int val = p.ReadShort();
        int mhp = p.ReadShort();
        int mmp = p.ReadShort();
        int hit = p.ReadShort();
        int mw = p.ReadInt();
        PointChangeEvent?.Invoke(type, val, mhp, mmp, hit, mw);
    }

    private void HandleGoldChange(Packet p)
    {
        if (p.RemainingBytes < 9) return;
        p.ReadByte();
        p.ReadInt();
        GoldChangeEvent?.Invoke(p.ReadInt());
    }

    private void HandleExpChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 4 && p.RemainingBytes >= 8) ExpChangeEvent?.Invoke(p.ReadLong());
        else if (sub == 5 && p.RemainingBytes >= 8) DeathExpLossEvent?.Invoke(p.ReadLong());
    }

    private static bool TryReadItemStats(Packet p, out DerivedStats stats)
    {
        stats = new DerivedStats();
        if (p.RemainingBytes < 34)
            return false;

        stats.TotalHit = p.ReadShort();
        stats.TotalAc = p.ReadShort();
        stats.MaxWeight = p.ReadInt();
        p.ReadByte(); p.ReadByte();
        stats.MaxHp = p.ReadShort();
        stats.MaxMp = p.ReadShort();
        stats.StrBonus = p.ReadShort();
        stats.StaBonus = p.ReadShort();
        stats.DexBonus = p.ReadShort();
        stats.IntBonus = p.ReadShort();
        stats.ChaBonus = p.ReadShort();
        stats.FireR = p.ReadShort();
        stats.ColdR = p.ReadShort();
        stats.LightningR = p.ReadShort();
        stats.MagicR = p.ReadShort();
        stats.DiseaseR = p.ReadShort();
        stats.PoisonR = p.ReadShort();
        return true;
    }

    private void ApplyPendingItemMove()
    {
        if (_pendingItemMove is not { } move)
            return;
        _pendingItemMove = null;
        if (!TryResolveMoveSlots(move.Dir, move.Src, move.Dst, out int from, out int to))
            return;
        EnsureLastInventoryLength(Math.Max(from, to) + 1);
        var e = LastEnter;
        (e.Inventory[from], e.Inventory[to]) = (e.Inventory[to], e.Inventory[from]);
        LastEnter = e;
        RefreshLastGear();
    }

    private void RefreshLastGear()
    {
        var e = LastEnter;
        var inv = e.Inventory;
        var gear = e.Gear is { Length: InventoryConstants.VisualSlotCount } existing
            ? existing
            : new int[InventoryConstants.VisualSlotCount];
        for (int i = 0; i < InventoryConstants.VisualSlots.Length; i++)
        {
            int slot = InventoryConstants.VisualSlots[i];
            gear[i] = inv != null && slot < inv.Length ? inv[slot].ItemId : 0;
        }
        e.Gear = gear;
        LastEnter = e;
    }

    private void ApplyLastStats(DerivedStats stats)
    {
        Vitals.ApplyMaxima(stats.MaxHp, stats.MaxMp);
        Sheet.ApplyDerived(stats);
    }

    private static bool TryResolveMoveSlots(byte dir, byte src, byte dst, out int from, out int to)
    {
        from = to = -1;
        switch (dir)
        {
            case 1: from = InventoryConstants.InventoryStart + src; to = dst; return src < InventoryConstants.HaveMax && dst < InventoryConstants.SlotMax;
            case 2: from = src; to = InventoryConstants.InventoryStart + dst; return src < InventoryConstants.SlotMax && dst < InventoryConstants.HaveMax;
            case 3: from = InventoryConstants.InventoryStart + src; to = InventoryConstants.InventoryStart + dst; return src < InventoryConstants.HaveMax && dst < InventoryConstants.HaveMax;
            case 4: from = src; to = dst; return src < InventoryConstants.SlotMax && dst < InventoryConstants.SlotMax;
            default: return false;
        }
    }

    private void ApplyLastInventoryGridRefresh(ItemSlot[] items)
    {
        EnsureLastInventoryLength(InventoryConstants.InventoryStart + items.Length);
        var e = LastEnter;
        for (int i = 0; i < items.Length; i++)
            e.Inventory[InventoryConstants.InventoryStart + i] = items[i];
        LastEnter = e;
    }

    public void MirrorInventorySlot(int absSlot, ItemSlot item) => SetLastInventorySlot(absSlot, item);

    public void RaiseGold(int total) => GoldChangeEvent?.Invoke(total);

    private void SetLastInventorySlot(int absSlot, ItemSlot item)
    {
        if (absSlot < 0) return;
        EnsureLastInventoryLength(absSlot + 1);
        if (item.ItemId == 0 || item.Count == 0)
            item = default;
        var e = LastEnter;
        e.Inventory[absSlot] = item;
        LastEnter = e;
        if (absSlot < InventoryConstants.SlotMax || InventoryConstants.IsCospreSlot(absSlot))
            RefreshLastGear();
    }

    private void RaiseItemGained(int absSlot, int itemId, int newCount)
    {
        if (itemId == 0 || newCount <= 0) return;
        var inv = LastEnter.Inventory;
        int had = inv != null && absSlot >= 0 && absSlot < inv.Length && inv[absSlot].ItemId == itemId
            ? inv[absSlot].Count
            : 0;
        int gained = newCount - had;
        if (gained > 0) ItemGainedEvent?.Invoke(itemId, gained);
    }

    private void PreserveLastDurability(int absSlot, ref ItemSlot item)
    {
        if (item.Durability != 0) return;

        var inv = LastEnter.Inventory;
        if (inv != null && absSlot >= 0 && absSlot < inv.Length
            && inv[absSlot].ItemId == item.ItemId && inv[absSlot].Durability != 0)
        {
            item.Durability = inv[absSlot].Durability;
            return;
        }

        var def = LibreKO.Domain.ItemData.Get(item.ItemId);
        if (def != null && def.Duration > 1)
            item.Durability = (short)def.Duration;
    }

    private void EnsureLastInventoryLength(int length)
    {
        var e = LastEnter;
        var inv = e.Inventory ?? System.Array.Empty<ItemSlot>();
        if (inv.Length < length)
            System.Array.Resize(ref inv, length);
        e.Inventory = inv;
        LastEnter = e;
    }

    private static int VisualGearIndexForLookSlot(int lookSlot)
        => System.Array.IndexOf(InventoryConstants.VisualSlots, lookSlot);
}
