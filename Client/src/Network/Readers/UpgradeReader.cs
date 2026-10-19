namespace LibreKO.Network.Readers;

internal sealed class UpgradeReader : IPacketReader
{
    private readonly ItemUpgradeSession itemUpgrade;
    private readonly ItemsSession items;
    private readonly NpcQuestSession npcQuest;
    private readonly SelfSession self;

    public UpgradeReader(ItemUpgradeSession itemUpgrade, ItemsSession items, NpcQuestSession npcQuest, SelfSession self)
    {
        this.itemUpgrade = itemUpgrade;
        this.items = items;
        this.npcQuest = npcQuest;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<ItemUpgradeSubOpcode>(GameOpcodes.GS_ITEM_UPGRADE, HandleItemUpgrade);
    }

    private void HandleItemUpgrade(Packet p, ItemUpgradeSubOpcode sub)
    {
        switch (sub)
        {
            case ItemUpgradeSubOpcode.AnvilOpen:
                if (p.RemainingBytes >= 4)
                    itemUpgrade.RaiseUpgradeOpen(p.ReadInt());
                break;
            case ItemUpgradeSubOpcode.Upgrade:
            case ItemUpgradeSubOpcode.UpgradeAccessories:
            case ItemUpgradeSubOpcode.UpgradeRebirth:
                HandleUpgradeResult(p, sub);
                break;
            case ItemUpgradeSubOpcode.ItemSeal:
                HandleSealResult(p);
                break;
            default:
                npcQuest.RaiseNpcWindow(GameOpcodes.GS_ITEM_UPGRADE);
                break;
        }
    }

    private void HandleSealResult(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        var sealType = (ItemSealType)p.ReadByte();
        var result = (ItemSealResult)p.ReadByte();
        int itemId = 0, srcPos = -1;
        if (p.RemainingBytes >= 5)
        {
            itemId = p.ReadInt();
            srcPos = p.ReadByte();
        }
        itemUpgrade.RaiseItemSeal(sealType, result, itemId, srcPos);
    }

    private void HandleUpgradeResult(Packet p, ItemUpgradeSubOpcode sub)
    {
        if (p.RemainingBytes < 2) return;

        var upgradeType = (ItemUpgradeKind)p.ReadByte();
        var result = (ItemUpgradeResult)p.ReadByte();

        var slots = new UpgradeSlotResult[ItemUpgradeWire.SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            int itemId = 0;
            int pos = -1;
            if (p.RemainingBytes >= 5)
            {
                itemId = p.ReadInt();
                pos = unchecked((sbyte)p.ReadByte());
            }
            slots[i] = new UpgradeSlotResult(itemId, pos);
        }

        ApplyUpgradeResultToLastInventory(upgradeType, result, slots);

        itemUpgrade.RaiseUpgradeResult(new UpgradeResult(sub, upgradeType, result, slots));
    }

    private void ApplyUpgradeResultToLastInventory(ItemUpgradeKind upgradeType, ItemUpgradeResult result, UpgradeSlotResult[] slots)
    {
        foreach (var (abs, slot) in UpgradeBagChanges.For(upgradeType, result, slots, self.Inventory.Slots, ItemData.MaxDurabilityOf))
        {
            self.ApplyInventorySlot(abs, slot);
            items.RaiseInventorySlot(abs, slot);
        }
    }
}
