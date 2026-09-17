namespace LibreKO.Common.Enums;

public enum ItemMoveDirection : byte
{
    InventoryToSlot = 1,    // Equip item
    SlotToInventory = 2,    // Unequip item
    InventoryToInventory = 3, // Rearrange inventory
    SlotToSlot = 4,         // Swap equipped items
    InventoryToZone = 5,    // Drop item (handled by WIZ_ITEM_DROP)
    ZoneToInventory = 6,    // Pick up item (handled by WIZ_ITEM_GET)
    InventoryToCospre = 7,
    CospreToInventory = 8,
    InventoryToMagicBag = 9,
    MagicBagToInventory = 10,
    MagicBagToMagicBag = 11,
    InventoryToBagSlot = 12,
    BagSlotToInventory = 13,
}
