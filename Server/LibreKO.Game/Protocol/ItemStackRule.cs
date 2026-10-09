using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol;

public static class ItemStackRule
{
    public const ushort WholeStack = 0;

    public static bool SplitsAcross(ItemMoveDirection direction) =>
        direction is ItemMoveDirection.InventoryToMagicBag
            or ItemMoveDirection.MagicBagToInventory
            or ItemMoveDirection.MagicBagToMagicBag;

    public static bool Splits(ItemMoveDirection direction, ItemSlot source, ItemSlot destination, ItemData? data, ushort amount) =>
        SplitsAcross(direction)
        && data is { Countable: > 0 }
        && !source.IsEmpty
        && source.UniqueId == 0
        && amount > WholeStack
        && amount < source.Count
        && (destination.IsEmpty
            || destination.ItemId == source.ItemId
            && destination.Flag == source.Flag
            && destination.UniqueId == 0
            && amount + destination.Count <= InventoryConstants.MaxStackCount);

    public static bool Merges(ItemMoveDirection direction, ItemSlot source, ItemSlot destination, ItemData? data) =>
        direction is ItemMoveDirection.InventoryToInventory
            or ItemMoveDirection.InventoryToMagicBag
            or ItemMoveDirection.MagicBagToInventory
            or ItemMoveDirection.MagicBagToMagicBag
        && data is { Countable: > 0 }
        && !source.IsEmpty
        && destination.ItemId == source.ItemId
        && destination.Flag == source.Flag
        && source.UniqueId == 0
        && destination.UniqueId == 0
        && source.Count + destination.Count <= InventoryConstants.MaxStackCount;
}
