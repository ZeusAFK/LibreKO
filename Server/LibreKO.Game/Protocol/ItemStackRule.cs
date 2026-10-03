using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol;

public static class ItemStackRule
{
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
