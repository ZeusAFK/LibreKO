using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IItemInventoryRuleService
{
    bool TryResolveEquipmentPosition(byte position, out byte resolvedPosition);
    bool TryResolveMoveIndices(
        UserSession session,
        ItemData itemData,
        ItemMoveDirection direction,
        byte sourcePosition,
        byte destinationPosition,
        out int sourceIndex,
        out int destinationIndex);
    bool TryResolveRemoveIndex(byte type, byte position, out int absoluteIndex, out bool affectsEquipment);
    bool IsEquipmentChange(ItemMoveDirection direction);
    int GetItemLeavingEquipment(ItemMoveDirection direction, int sourceItemId, int destinationItemId);
    int GetItemEnteringEquipment(ItemMoveDirection direction, int sourceItemId, int destinationItemId);
    bool TryGetCospreVisualSlot(byte cosprePosition, out byte visualSlot);
}

public class ItemInventoryRuleService(IGameDataService gameDataService) : IItemInventoryRuleService
{
    private const byte ItemSlot1HEitherHand = 0;
    private const byte ItemSlot1HRightHand = 1;
    private const byte ItemSlot1HLeftHand = 2;
    private const byte ItemSlot2HRightHand = 3;
    private const byte ItemSlot2HLeftHand = 4;
    private const byte ItemSlotPauldron = 5;
    private const byte ItemSlotPads = 6;
    private const byte ItemSlotHelmet = 7;
    private const byte ItemSlotGloves = 8;
    private const byte ItemSlotBoots = 9;
    private const byte ItemSlotEarring = 10;
    private const byte ItemSlotNecklace = 11;
    private const byte ItemSlotRing = 12;
    private const byte ItemSlotBelt = 14;
    private const byte ItemSlotPet = 20;
    private const byte ItemSlotBag = 25;

    private const byte CospreCodeBase = 100;
    private const byte CospreCodeGloveEither = 0;
    private const byte CospreCodeGloveRight = 1;
    private const byte CospreCodeGloveLeft = 2;
    private const byte CospreCodePauldron = 5;
    private const byte CospreCodeHelmet = 7;
    private const byte CospreCodeWings = 10;
    private const byte CospreCodeFairy = 11;
    private const byte CospreCodeTattoo = 12;
    private const byte CospreCodeTalisman = 13;
    private const byte CospreCodeEmblem = 14;
    private const byte CospreCodeTattooAlt = 27;

    public bool TryResolveEquipmentPosition(byte position, out byte resolvedPosition)
    {
        // Client slot indices match storage indices directly (0-13)
        resolvedPosition = position;
        return position < InventoryConstants.SlotMax;
    }

    public bool TryResolveMoveIndices(
        UserSession session,
        ItemData itemData,
        ItemMoveDirection direction,
        byte sourcePosition,
        byte destinationPosition,
        out int sourceIndex,
        out int destinationIndex)
    {
        sourceIndex = -1;
        destinationIndex = -1;

        switch (direction)
        {
            case ItemMoveDirection.InventoryToSlot:
                if (destinationPosition >= InventoryConstants.SlotMax
                    || sourcePosition >= InventoryConstants.HaveMax
                    || !IsValidEquipmentDestination(session, itemData, destinationPosition))
                {
                    return false;
                }

                sourceIndex = InventoryConstants.InventoryStart + sourcePosition;
                destinationIndex = destinationPosition;
                return true;

            case ItemMoveDirection.SlotToInventory:
                if (destinationPosition >= InventoryConstants.HaveMax || sourcePosition >= InventoryConstants.SlotMax)
                    return false;

                sourceIndex = sourcePosition;
                destinationIndex = InventoryConstants.InventoryStart + destinationPosition;
                return true;

            case ItemMoveDirection.InventoryToInventory:
                if (destinationPosition >= InventoryConstants.HaveMax || sourcePosition >= InventoryConstants.HaveMax)
                    return false;

                sourceIndex = InventoryConstants.InventoryStart + sourcePosition;
                destinationIndex = InventoryConstants.InventoryStart + destinationPosition;
                return true;

            case ItemMoveDirection.SlotToSlot:
                if (destinationPosition >= InventoryConstants.SlotMax
                    || sourcePosition >= InventoryConstants.SlotMax
                    || !IsValidEquipmentDestination(session, itemData, destinationPosition))
                {
                    return false;
                }

                sourceIndex = sourcePosition;
                destinationIndex = destinationPosition;
                return true;

            case ItemMoveDirection.InventoryToCospre:
                if (destinationPosition >= InventoryConstants.CospreMax
                    || sourcePosition >= InventoryConstants.HaveMax
                    || !IsValidCospreDestination(itemData, destinationPosition))
                {
                    return false;
                }

                sourceIndex = InventoryConstants.InventoryStart + sourcePosition;
                destinationIndex = InventoryConstants.CospreStart + destinationPosition;
                return true;

            case ItemMoveDirection.CospreToInventory:
                if (destinationPosition >= InventoryConstants.HaveMax
                    || sourcePosition >= InventoryConstants.CospreMax)
                {
                    return false;
                }

                sourceIndex = InventoryConstants.CospreStart + sourcePosition;
                destinationIndex = InventoryConstants.InventoryStart + destinationPosition;
                return true;

            case ItemMoveDirection.InventoryToBagSlot:
                if (destinationPosition >= InventoryConstants.BagSlotMax
                    || sourcePosition >= InventoryConstants.HaveMax
                    || itemData.Slot != ItemSlotBag)
                {
                    return false;
                }

                sourceIndex = InventoryConstants.InventoryStart + sourcePosition;
                destinationIndex = InventoryConstants.BagSlotFor(destinationPosition);

                if (!session.Inventory[destinationIndex].IsEmpty)
                    return false;

                return true;

            case ItemMoveDirection.BagSlotToInventory:
                if (destinationPosition >= InventoryConstants.HaveMax
                    || sourcePosition >= InventoryConstants.BagSlotMax
                    || !IsMagicBagEmpty(session, sourcePosition))
                {
                    return false;
                }

                sourceIndex = InventoryConstants.BagSlotFor(sourcePosition);
                destinationIndex = InventoryConstants.InventoryStart + destinationPosition;
                return true;

            case ItemMoveDirection.InventoryToMagicBag:
                if (destinationPosition >= InventoryConstants.MagicBagTotal
                    || sourcePosition >= InventoryConstants.HaveMax
                    || !HasEquippedBag(session, destinationPosition))
                {
                    return false;
                }

                sourceIndex = InventoryConstants.InventoryStart + sourcePosition;
                destinationIndex = InventoryConstants.MagicBagStart + destinationPosition;
                return true;

            case ItemMoveDirection.MagicBagToInventory:
                if (destinationPosition >= InventoryConstants.HaveMax
                    || sourcePosition >= InventoryConstants.MagicBagTotal
                    || !HasEquippedBag(session, sourcePosition))
                {
                    return false;
                }

                sourceIndex = InventoryConstants.MagicBagStart + sourcePosition;
                destinationIndex = InventoryConstants.InventoryStart + destinationPosition;
                return true;

            case ItemMoveDirection.MagicBagToMagicBag:
                if (destinationPosition >= InventoryConstants.MagicBagTotal
                    || sourcePosition >= InventoryConstants.MagicBagTotal
                    || !HasEquippedBag(session, sourcePosition)
                    || !HasEquippedBag(session, destinationPosition))
                {
                    return false;
                }

                sourceIndex = InventoryConstants.MagicBagStart + sourcePosition;
                destinationIndex = InventoryConstants.MagicBagStart + destinationPosition;
                return true;
        }

        return false;
    }

    public bool TryResolveRemoveIndex(byte type, byte position, out int absoluteIndex, out bool affectsEquipment)
    {
        absoluteIndex = -1;
        affectsEquipment = false;

        switch (type)
        {
            case 0:
            case 2:
                if (position >= InventoryConstants.HaveMax)
                    return false;

                absoluteIndex = InventoryConstants.InventoryStart + position;
                return true;

            case 1:
                if (position >= InventoryConstants.SlotMax)
                    return false;

                absoluteIndex = position;
                affectsEquipment = true;
                return true;

            default:
                return false;
        }
    }

    public bool IsEquipmentChange(ItemMoveDirection direction) =>
        direction is ItemMoveDirection.InventoryToSlot
            or ItemMoveDirection.SlotToInventory
            or ItemMoveDirection.SlotToSlot
            or ItemMoveDirection.InventoryToCospre
            or ItemMoveDirection.CospreToInventory;

    public int GetItemLeavingEquipment(ItemMoveDirection direction, int sourceItemId, int destinationItemId) =>
        direction switch
        {
            ItemMoveDirection.InventoryToSlot => destinationItemId,
            ItemMoveDirection.SlotToInventory => sourceItemId,
            _ => 0
        };

    public int GetItemEnteringEquipment(ItemMoveDirection direction, int sourceItemId, int destinationItemId) =>
        direction switch
        {
            ItemMoveDirection.InventoryToSlot => sourceItemId,
            ItemMoveDirection.SlotToInventory => destinationItemId,
            _ => 0
        };

    public bool TryGetCospreVisualSlot(byte cosprePosition, out byte visualSlot)
    {
        visualSlot = cosprePosition switch
        {
            InventoryConstants.CosPosWing => (byte)InventoryConstants.CosWing,
            InventoryConstants.CosPosHelmet => (byte)InventoryConstants.CosHelmet,
            InventoryConstants.CosPosGloveRight => (byte)InventoryConstants.CosGloveRight,
            InventoryConstants.CosPosGloveLeft => (byte)InventoryConstants.CosGloveLeft,
            InventoryConstants.CosPosPauldron => (byte)InventoryConstants.CosPauldron,
            InventoryConstants.CosPosEmblem => (byte)InventoryConstants.CosEmblem,
            InventoryConstants.CosPosFairy => (byte)InventoryConstants.CosFairy,
            InventoryConstants.CosPosTattoo => (byte)InventoryConstants.CosTattoo,
            InventoryConstants.CosPosTalisman => (byte)InventoryConstants.CosTalisman,
            _ => byte.MaxValue
        };

        return visualSlot != byte.MaxValue;
    }

    private bool IsValidEquipmentDestination(UserSession session, ItemData itemData, byte destinationPosition)
    {
        var isOneHandedItem = false;
        switch (itemData.Slot)
        {
            case ItemSlot1HEitherHand:
                if (destinationPosition != InventoryConstants.RightHand && destinationPosition != InventoryConstants.LeftHand)
                    return false;
                isOneHandedItem = true;
                break;

            case ItemSlot1HRightHand:
                if (destinationPosition != InventoryConstants.RightHand)
                    return false;
                isOneHandedItem = true;
                break;

            case ItemSlot2HRightHand:
                if (destinationPosition != InventoryConstants.RightHand
                    || session.Inventory[InventoryConstants.LeftHand].ItemId != 0)
                {
                    return false;
                }

                break;

            case ItemSlot1HLeftHand:
                if (destinationPosition != InventoryConstants.LeftHand)
                    return false;
                isOneHandedItem = true;
                break;

            case ItemSlot2HLeftHand:
                if (destinationPosition != InventoryConstants.LeftHand
                    || session.Inventory[InventoryConstants.RightHand].ItemId != 0)
                {
                    return false;
                }

                break;

            case ItemSlotPauldron:
                if (destinationPosition != InventoryConstants.Breast)
                    return false;
                break;

            case ItemSlotPads:
                if (destinationPosition != InventoryConstants.Leg)
                    return false;
                break;

            case ItemSlotHelmet:
                if (destinationPosition != InventoryConstants.Head)
                    return false;
                break;

            case ItemSlotGloves:
                if (destinationPosition != InventoryConstants.Glove)
                    return false;
                break;

            case ItemSlotBoots:
                if (destinationPosition != InventoryConstants.Foot)
                    return false;
                break;

            case ItemSlotEarring:
                if (destinationPosition != InventoryConstants.RightEar && destinationPosition != InventoryConstants.LeftEar)
                    return false;
                break;

            case ItemSlotNecklace:
                if (destinationPosition != InventoryConstants.Neck)
                    return false;
                break;

            case ItemSlotRing:
                if (destinationPosition != InventoryConstants.RightRing && destinationPosition != InventoryConstants.LeftRing)
                    return false;
                break;

            case ItemSlotPet:
                if (destinationPosition != InventoryConstants.Pet)
                    return false;
                break;

            case ItemSlotBelt:
                if (destinationPosition != InventoryConstants.Waist)
                    return false;
                break;

            default:
                return false;
        }

        if (!isOneHandedItem)
            return true;

        var otherHandPosition = destinationPosition == InventoryConstants.LeftHand
            ? InventoryConstants.RightHand
            : InventoryConstants.LeftHand;
        var otherHandItem = session.Inventory[otherHandPosition];
        if (otherHandItem.IsEmpty)
            return true;

        var otherHandData = gameDataService.GetItem(otherHandItem.ItemId);
        return otherHandData?.Slot is not (ItemSlot2HRightHand or ItemSlot2HLeftHand);
    }

    private static bool IsValidCospreDestination(ItemData itemData, byte destinationPosition)
    {
        if (itemData.Slot < CospreCodeBase)
            return false;

        return (itemData.Slot % CospreCodeBase) switch
        {
            CospreCodeWings => destinationPosition == InventoryConstants.CosPosWing,
            CospreCodeHelmet => destinationPosition == InventoryConstants.CosPosHelmet,
            CospreCodeGloveEither => destinationPosition
                is InventoryConstants.CosPosGloveRight or InventoryConstants.CosPosGloveLeft,
            CospreCodeGloveRight => destinationPosition == InventoryConstants.CosPosGloveRight,
            CospreCodeGloveLeft => destinationPosition == InventoryConstants.CosPosGloveLeft,
            CospreCodePauldron => destinationPosition == InventoryConstants.CosPosPauldron,
            CospreCodeEmblem => destinationPosition == InventoryConstants.CosPosEmblem,
            CospreCodeFairy => destinationPosition == InventoryConstants.CosPosFairy,
            CospreCodeTattoo or CospreCodeTattooAlt => destinationPosition == InventoryConstants.CosPosTattoo,
            CospreCodeTalisman => destinationPosition == InventoryConstants.CosPosTalisman,
            _ => false
        };
    }

    private static bool HasEquippedBag(UserSession session, byte magicBagPosition)
    {
        var bagSlot = InventoryConstants.BagSlotFor(
            InventoryConstants.BagIndexForMagicBagPosition(magicBagPosition));
        return !session.Inventory[bagSlot].IsEmpty;
    }

    private static bool IsMagicBagEmpty(UserSession session, int bagIndex)
    {
        var bagStart = InventoryConstants.MagicBagPageStart(bagIndex);
        for (var index = 0; index < InventoryConstants.MagicBagMax; index++)
        {
            if (!session.Inventory[bagStart + index].IsEmpty)
                return false;
        }

        return true;
    }
}
