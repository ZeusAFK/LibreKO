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
    public const byte InventoryToPet = 12;
    public const byte PetToInventory = 13;
    public const byte InventoryToBagSlot = 14;
    public const byte BagSlotToInventory = 15;

    public const byte MoveRequest = 1;
    public const byte ArrangeRequest = 2;

    public const byte None = 0;

    public const ushort WholeStack = 0;

    public enum Region { Equip, Grid, Cospre, BagSlot, MagicBag }

    public static bool IsCarried(int abs) => RegionOf(abs) is Region.Grid or Region.MagicBag;

    public static bool TryResolveSlots(byte direction, byte source, byte destination, out int from, out int to)
    {
        (int Start, int Count) equip = (0, InventoryConstants.SlotMax),
            grid = (InventoryConstants.InventoryStart, InventoryConstants.HaveMax),
            costume = (InventoryConstants.CospreStart, InventoryConstants.CospreMax),
            bag = (InventoryConstants.BagSlotStart, InventoryConstants.BagSlotMax),
            contents = (InventoryConstants.MagicBagStart, InventoryConstants.MagicBagTotal),
            none = (0, 0);
        var (origin, target) = direction switch
        {
            InventoryToSlot => (grid, equip), SlotToInventory => (equip, grid),
            InventoryToInventory => (grid, grid), SlotToSlot => (equip, equip),
            InventoryToCospre => (grid, costume), CospreToInventory => (costume, grid),
            InventoryToBagSlot => (grid, bag), BagSlotToInventory => (bag, grid),
            InventoryToMagicBag => (grid, contents), MagicBagToInventory => (contents, grid),
            MagicBagToMagicBag => (contents, contents),
            _ => (none, none),
        };
        from = to = -1;
        if (source >= origin.Count || destination >= target.Count) return false;
        from = origin.Start + source;
        to = target.Start + destination;
        return true;
    }

    public static void ApplyConfirmed(ItemSlot[] inventory, byte direction, int from, int to, int countable, ushort amount = WholeStack)
    {
        var source = inventory[from];
        var destination = inventory[to];
        if (from != to && amount != WholeStack && amount < source.Count)
        {
            if (destination.IsEmpty)
            {
                destination = source;
                destination.Count = (short)amount;
            }
            else
                destination.Count += (short)amount;
            source.Count -= (short)amount;
            inventory[from] = source;
            inventory[to] = destination;
        }
        else if (from != to && Merges(direction, source, destination, countable))
        {
            destination.Count += source.Count;
            inventory[to] = destination;
            inventory[from] = default;
        }
        else
            (inventory[from], inventory[to]) = (destination, source);
    }

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

    public static bool Merges(byte direction, ItemSlot source, ItemSlot destination, int countable) =>
        direction is InventoryToInventory or InventoryToMagicBag or MagicBagToInventory or MagicBagToMagicBag
        && countable > 0
        && !source.IsEmpty
        && destination.ItemId == source.ItemId
        && destination.Flag == source.Flag
        && !source.IsLinked
        && !destination.IsLinked
        && source.Count + destination.Count <= Inventory.StackMax;

    public static bool SplitsAcross(byte direction) =>
        direction is InventoryToMagicBag or MagicBagToInventory or MagicBagToMagicBag;

    public static bool Splits(byte direction, ItemSlot source, ItemSlot destination, int countable, int amount) =>
        SplitsAcross(direction)
        && countable > 0
        && !source.IsEmpty
        && !source.IsLinked
        && amount > WholeStack
        && amount < source.Count
        && (destination.IsEmpty
            || destination.ItemId == source.ItemId
            && destination.Flag == source.Flag
            && !destination.IsLinked
            && amount + destination.Count <= Inventory.StackMax);
}
