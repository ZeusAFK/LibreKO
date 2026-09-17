using System;
using LibreKO.Domain;

namespace LibreKO.Network;

public readonly record struct UpgradeSlotResult(int ItemId, int Position);

public readonly record struct UpgradeResult(
    byte SubOpcode,
    byte UpgradeType,
    byte ResultCode,
    UpgradeSlotResult[] Slots);

public partial class Net
{
    public event Action<int>? UpgradeOpenEvent;
    public event Action<UpgradeResult>? UpgradeResultEvent;
    public event Action<ItemSealType, ItemSealResult, int, int>? ItemSealEvent;

    private const byte UpgradeOpenSub = 1;
    private const byte UpgradeNormalSub = 2;
    private const byte UpgradeAccessorySub = 3;
    private const byte UpgradeReverseSub = 7;
    private const byte UpgradeSealSub = 8;
    private const int SealRequestNoWindow = -1;
    private const byte UpgradeRequestNormal = 1;
    private const byte UpgradeRequestPreview = 2;
    private const byte UpgradeResultFailed = 0;
    private const byte UpgradeResultSucceeded = 1;
    private const int UpgradeSlotCount = 10;
    private const int AccessoryScrollFirst = 379159000;
    private const int AccessoryScrollLast = 379164000;
    private const int ReverseScroll = 379256000;

    private void HandleItemUpgrade(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case UpgradeOpenSub:
                if (p.RemainingBytes >= 4)
                    UpgradeOpenEvent?.Invoke(p.ReadInt());
                break;
            case UpgradeNormalSub:
            case UpgradeAccessorySub:
            case UpgradeReverseSub:
                HandleUpgradeResult(p, sub);
                break;
            case UpgradeSealSub:
                HandleSealResult(p);
                break;
            case PieceChangeOpenSub:
                HandlePieceChangeOpen(p);
                break;
            case PieceChangeExchangeSub:
                HandlePieceExchangeResult(p);
                break;
            default:
                NpcWindowEvent?.Invoke(GameOpcodes.GS_ITEM_UPGRADE);
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
        ItemSealEvent?.Invoke(sealType, result, itemId, srcPos);
    }

    public void SendItemSeal(ItemSealType sealType, int itemId, byte srcPos, string code = "")
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte(UpgradeSealSub);
        p.WriteByte((byte)sealType);
        p.WriteInt(SealRequestNoWindow);
        p.WriteInt(itemId);
        p.WriteByte(srcPos);
        p.WriteString(code);
        _conn.Send(p);
    }

    private void HandleUpgradeResult(Packet p, byte sub)
    {
        if (p.RemainingBytes < 2) return;

        byte upgradeType = p.ReadByte();
        byte result = p.ReadByte();

        var slots = new UpgradeSlotResult[UpgradeSlotCount];
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

        UpgradeResultEvent?.Invoke(new UpgradeResult(sub, upgradeType, result, slots));
    }

    private void ApplyUpgradeResultToLastInventory(byte upgradeType, byte result, UpgradeSlotResult[] slots)
    {
        if (upgradeType != UpgradeRequestNormal || slots.Length == 0)
            return;

        var origin = slots[0];
        if (origin.Position >= 0 && origin.Position < InventoryConstants.HaveMax)
        {
            int abs = InventoryConstants.InventoryStart + origin.Position;
            if (result == UpgradeResultSucceeded && origin.ItemId != 0)
            {
                var def = ItemData.Get(origin.ItemId);
                var updated = new ItemSlot
                {
                    ItemId = origin.ItemId,
                    Count = 1,
                    Durability = (short)(def?.Duration ?? 0),
                };
                SetLastInventorySlot(abs, updated);
                InventorySlotEvent?.Invoke(abs, updated);
            }
            else if (result == UpgradeResultFailed)
            {
                SetLastInventorySlot(abs, default);
                InventorySlotEvent?.Invoke(abs, default);
            }
        }

        for (int i = 1; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (slot.Position < 0 || slot.Position >= InventoryConstants.HaveMax)
                continue;

            int abs = InventoryConstants.InventoryStart + slot.Position;
            var inv = LastEnter.Inventory;
            var current = inv != null && abs < inv.Length ? inv[abs] : default;
            if (current.ItemId == 0 || current.ItemId != slot.ItemId)
                continue;

            if (current.Count > 1)
                current.Count--;
            else
                current = default;

            SetLastInventorySlot(abs, current);
            InventorySlotEvent?.Invoke(abs, current);
        }
    }

    public static byte UpgradeSubOpcodeFor(int[] itemIds)
    {
        for (int i = 1; i < itemIds.Length; i++)
        {
            if (itemIds[i] >= AccessoryScrollFirst && itemIds[i] <= AccessoryScrollLast)
                return UpgradeAccessorySub;
            if (itemIds[i] == ReverseScroll)
                return UpgradeReverseSub;
        }
        return UpgradeNormalSub;
    }

    public void SendUpgradeRequest(int npcId, int[] itemIds, int[] positions, bool preview = false)
    {
        SendUpgrade(
            UpgradeSubOpcodeFor(itemIds),
            preview ? UpgradeRequestPreview : UpgradeRequestNormal,
            npcId,
            itemIds,
            positions);
    }

    private void SendUpgrade(byte subOpcode, byte upgradeType, int npcId, int[] itemIds, int[] positions)
    {
        var p = new Packet(GameOpcodes.GS_ITEM_UPGRADE);
        p.WriteByte(subOpcode);
        p.WriteByte(upgradeType);
        p.WriteInt(npcId);
        for (int i = 0; i < UpgradeSlotCount; i++)
        {
            p.WriteInt(i < itemIds.Length ? itemIds[i] : 0);
            int pos = i < positions.Length ? positions[i] : -1;
            p.WriteByte(pos >= 0 ? (byte)pos : byte.MaxValue);
        }
        _conn.Send(p);
    }
}
