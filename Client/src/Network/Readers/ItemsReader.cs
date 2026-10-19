namespace LibreKO.Network.Readers;

internal sealed class ItemsReader : IPacketReader
{
    private readonly EntitiesSession entities;
    private readonly ItemsSession items;
    private readonly LootSession loot;
    private readonly ProgressSession progress;
    private readonly SelfSession self;

    private const int CountChangeEntryBytes = 17;
    private const byte CountChangeNewItemFlag = 100;

    public ItemsReader(EntitiesSession entities, ItemsSession items, LootSession loot, ProgressSession progress, SelfSession self)
    {
        this.entities = entities;
        this.items = items;
        this.loot = loot;
        this.progress = progress;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ITEM_MOVE, HandleItemMove);
        dispatcher.On(GameOpcodes.GS_ITEM_COUNT_CHANGE, HandleItemCountChange);
        dispatcher.On(GameOpcodes.GS_ITEM_GET, HandleItemGet);
        dispatcher.On(GameOpcodes.GS_ITEM_REMOVE, HandleItemRemove);
        dispatcher.On(GameOpcodes.GS_USERLOOK_CHANGE, HandleUserLookChange);
        dispatcher.On(GameOpcodes.GS_POINT_CHANGE, HandlePointChange);
        dispatcher.On(GameOpcodes.GS_GOLD_CHANGE, HandleGoldChange);
        dispatcher.On(GameOpcodes.GS_EXP_CHANGE, HandleExpChange);
    }

    private void HandleItemMove(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var command = (ItemMoveSubOpcode)p.ReadByte();
        if (command == ItemMoveSubOpcode.Move)
        {
            if (p.RemainingBytes < 1) return;
            bool ok = p.ReadByte() == 1;
            if (ok) ApplyPendingItemMove();
            else items.PendingMove = null;
            items.RaiseItemMoveResult(ok);
            if (ok && ItemStatsBlock.TryRead(p, out var stats))
            {
                self.ApplyDerivedStats(stats);
                items.RaiseItemStats(stats);
            }
        }
        else if (command == ItemMoveSubOpcode.ArrangeInventory)
        {
            if (p.RemainingBytes < 1) return;
            bool ok = p.ReadByte() == 1;
            if (!ok)
            {
                items.RaiseInventoryArrangeFailed();
                return;
            }
            if (p.RemainingBytes < InventoryConstants.HaveMax * 19) return;
            var grid = new ItemSlot[InventoryConstants.HaveMax];
            for (int i = 0; i < grid.Length; i++)
            {
                int itemId = p.ReadInt();
                short dur = WireNumbers.ClampToShort(p.ReadUShort());
                short count = WireNumbers.ClampToShort(p.ReadUShort());
                byte flag = p.ReadByte(); p.ReadUShort(); p.ReadInt();
                int expiresAt = p.ReadInt();
                grid[i] = new ItemSlot
                {
                    ItemId = itemId,
                    Durability = dur,
                    Count = count,
                    Flag = flag,
                    ExpiresAt = expiresAt,
                };
            }
            self.ApplyInventoryGrid(grid);
            items.RaiseInventoryGridRefresh(grid);
        }
    }

    private void HandleItemCountChange(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int entries = p.ReadShort();
        for (int i = 0; i < entries; i++)
        {
            if (p.RemainingBytes < CountChangeEntryBytes) return;
            var kind = (ItemCountChangeKind)p.ReadByte();
            byte pos = p.ReadByte();
            int itemId = p.ReadInt();
            short count = WireNumbers.ClampToShort(p.ReadInt());
            byte flag = p.ReadByte();
            short dur = p.ReadShort();
            p.ReadInt();
            if (kind is ItemCountChangeKind.Received or ItemCountChangeKind.ReceivedAlternate) items.RaiseItemReceived(itemId, false);
            else if (flag == CountChangeNewItemFlag) items.RaiseItemReceived(itemId, true);
            if (kind == ItemCountChangeKind.None || pos >= InventoryConstants.HaveMax) continue;

            int abs = InventoryConstants.InventoryStart + pos;
            var item = new ItemSlot { ItemId = itemId, Durability = dur, Count = count };
            RaiseItemGained(abs, itemId, count);
            self.ApplyInventorySlot(abs, item);
            items.RaiseInventorySlot(abs, item);
        }
    }

    private void HandleItemGet(Packet p)
    {
        if (ItemGetReply.Read(p) is not { } reply) return;
        if (reply.Result == ItemGetResult.Failed)
        {
            loot.RaiseLootFail(reply.Failure);
            return;
        }
        if (reply.Result == ItemGetResult.PartyMember)
        {
            loot.RaisePartyLoot(reply.Looter ?? "", reply.ItemId);
            loot.RaiseLootTaken(reply.BundleId, reply.BundleSlot, reply.ItemId);
            return;
        }
        if (reply.Result is ItemGetResult.TemporaryKept or ItemGetResult.TemporaryLost)
        {
            loot.RaiseTemporaryLoot(reply.ItemId, reply.Result == ItemGetResult.TemporaryLost);
            return;
        }
        if (reply.IsGold)
        {
            loot.RaiseLootGold(reply.Money);
            progress.ApplyGold(reply.Money);
        }
        else if (reply.Position < InventoryConstants.HaveMax)
        {
            loot.RaiseLootItem(reply.ItemId);
            int abs = InventoryConstants.InventoryStart + reply.Position;
            var item = new ItemSlot { ItemId = reply.ItemId, Count = reply.Count };
            self.PreserveDurability(abs, ref item);
            RaiseItemGained(abs, reply.ItemId, reply.Count);
            self.SetInventorySlot(abs, item);
            items.RaiseInventorySlot(abs, item);
        }
        loot.RaiseLootTaken(reply.BundleId, reply.BundleSlot, reply.ItemId);
    }

    private void HandleItemRemove(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        bool ok = p.ReadByte() == 1;
        var reason = !ok && p.RemainingBytes >= 1 ? (ItemRemoveReason)p.ReadByte() : ItemRemoveReason.SystemError;
        if (ok && items.PendingRemoveSlot >= 0)
        {
            int slot = items.PendingRemoveSlot;
            self.SetInventorySlot(slot, default);
            items.RaiseInventorySlot(slot, default);
        }
        items.ApplyRemoveResult(ok, reason);
    }

    private void HandleUserLookChange(Packet p)
    {
        if (p.RemainingBytes < 9) return;
        int charId = p.ReadInt();
        int slot = p.ReadByte();
        int itemId = p.ReadInt();
        short durability = p.RemainingBytes >= 2 ? WireNumbers.ClampToShort(p.ReadUShort()) : (short)0;
        if (p.RemainingBytes >= 1) p.ReadByte();
        if (entities.TryGet(charId, out var e) && !e.IsNpc)
        {
            int gearIndex = VisualGearIndexForLookSlot(slot);
            if (gearIndex >= 0)
            {
                if (e.Gear.Length < InventoryConstants.VisualSlotCount)
                    System.Array.Resize(ref e.Gear, InventoryConstants.VisualSlotCount);
                e.Gear[gearIndex] = itemId;
            }
        }
        items.RaiseLookChange(charId, slot, itemId, durability);
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
        progress.RaisePointChange(new PointChangeReply(Type: type, Value: val, MaxHp: mhp, MaxMp: mmp, TotalHit: hit, MaxWeight: mw));
    }

    private void HandleGoldChange(Packet p)
    {
        if (GoldChangeReply.Read(p) is not { } reply) return;
        progress.RaiseGoldChangeLog(reply);
        if (reply.ChangesBalance) progress.ApplyGold(reply.Total);
    }

    private void HandleExpChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ExperienceSubOpcode)p.ReadByte();
        if (sub == ExperienceSubOpcode.ExpRateAward && p.RemainingBytes >= 6) progress.RaiseExpJackpot(p.ReadInt(), p.ReadUShort());
        else if (sub == ExperienceSubOpcode.CurrentExperience && p.RemainingBytes >= 8) progress.ApplyExp(p.ReadLong());
        else if (sub == ExperienceSubOpcode.DeathExperienceLoss && p.RemainingBytes >= 8) progress.RaiseDeathExpLoss(p.ReadLong());
    }

    private void ApplyPendingItemMove()
    {
        if (items.PendingMove is not { } move)
            return;
        items.PendingMove = null;
        if (!ItemMove.TryResolveSlots(move.Dir, move.Src, move.Dst, out int from, out int to))
            return;
        self.ApplyItemMove(move.Dir, from, to, move.Amount);
    }

    private void RaiseItemGained(int absSlot, int itemId, int newCount)
    {
        if (itemId == 0 || newCount <= 0) return;
        int had = self.Inventory.Holds(absSlot, itemId) ? self.Inventory[absSlot].Count : 0;
        int gained = newCount - had;
        if (gained > 0) items.RaiseItemGained(itemId, gained);
    }

    private static int VisualGearIndexForLookSlot(int lookSlot)
            => System.Array.IndexOf(InventoryConstants.VisualSlots, lookSlot);
}
