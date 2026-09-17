namespace LibreKO.Domain;

public static class ItemMove
{
    public const byte InventoryToSlot = 1;
    public const byte SlotToInventory = 2;
    public const byte InventoryToInventory = 3;
    public const byte SlotToSlot = 4;
    public const byte InventoryToZone = 5;
    public const byte ZoneToInventory = 6;
    public const byte InventoryToCospre = 7;
    public const byte CospreToInventory = 8;
    public const byte InventoryToMagicBag = 9;
    public const byte MagicBagToInventory = 10;
    public const byte MagicBagToMagicBag = 11;
    public const byte InventoryToBagSlot = 12;
    public const byte BagSlotToInventory = 13;

    public const byte MoveRequest = 1;
    public const byte ArrangeRequest = 2;

    public const byte None = 0;

    public enum Region { Equip, Grid, Cospre, BagSlot, MagicBag }

    public static Region RegionOf(int abs)
    {
        if (abs < InventoryConstants.InventoryStart) return Region.Equip;
        if (abs < InventoryConstants.CospreStart) return Region.Grid;
        if (abs < InventoryConstants.BagSlotStart) return Region.Cospre;
        if (abs < InventoryConstants.MagicBagStart) return Region.BagSlot;
        return Region.MagicBag;
    }

    public static int PositionIn(Region region, int abs) => region switch
    {
        Region.Equip => abs,
        Region.Grid => abs - InventoryConstants.InventoryStart,
        Region.Cospre => abs - InventoryConstants.CospreStart,
        Region.BagSlot => abs - InventoryConstants.BagSlotStart,
        _ => abs - InventoryConstants.MagicBagStart,
    };

    public static byte DirectionFor(Region from, Region to) => (from, to) switch
    {
        (Region.Grid, Region.Equip) => InventoryToSlot,
        (Region.Equip, Region.Grid) => SlotToInventory,
        (Region.Grid, Region.Grid) => InventoryToInventory,
        (Region.Equip, Region.Equip) => SlotToSlot,
        (Region.Grid, Region.Cospre) => InventoryToCospre,
        (Region.Cospre, Region.Grid) => CospreToInventory,
        (Region.Grid, Region.BagSlot) => InventoryToBagSlot,
        (Region.BagSlot, Region.Grid) => BagSlotToInventory,
        (Region.Grid, Region.MagicBag) => InventoryToMagicBag,
        (Region.MagicBag, Region.Grid) => MagicBagToInventory,
        (Region.MagicBag, Region.MagicBag) => MagicBagToMagicBag,
        _ => None,
    };
}
