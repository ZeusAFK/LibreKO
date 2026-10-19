namespace LibreKO.Domain;

public static class ItemMove
{
    public const ushort WholeStack = 0;

    public enum Region { Equip, Grid, Cospre, BagSlot, MagicBag }

    public static bool IsCarried(int abs) => RegionOf(abs) is Region.Grid or Region.MagicBag;

    public static bool TryResolveSlots(ItemMoveDirection direction, byte source, byte destination, out int from, out int to)
    {
        (int Start, int Count) equip = (0, InventoryConstants.SlotMax),
            grid = (InventoryConstants.InventoryStart, InventoryConstants.HaveMax),
            costume = (InventoryConstants.CospreStart, InventoryConstants.CospreMax),
            bag = (InventoryConstants.BagSlotStart, InventoryConstants.BagSlotMax),
            contents = (InventoryConstants.MagicBagStart, InventoryConstants.MagicBagTotal),
            none = (0, 0);
        var (origin, target) = direction switch
        {
            ItemMoveDirection.InventoryToSlot => (grid, equip), ItemMoveDirection.SlotToInventory => (equip, grid),
            ItemMoveDirection.InventoryToInventory => (grid, grid), ItemMoveDirection.SlotToSlot => (equip, equip),
            ItemMoveDirection.InventoryToCospre => (grid, costume), ItemMoveDirection.CospreToInventory => (costume, grid),
            ItemMoveDirection.InventoryToBagSlot => (grid, bag), ItemMoveDirection.BagSlotToInventory => (bag, grid),
            ItemMoveDirection.InventoryToMagicBag => (grid, contents), ItemMoveDirection.MagicBagToInventory => (contents, grid),
            ItemMoveDirection.MagicBagToMagicBag => (contents, contents),
            _ => (none, none),
        };
        from = to = -1;
        if (source >= origin.Count || destination >= target.Count) return false;
        from = origin.Start + source;
        to = target.Start + destination;
        return true;
    }

    public static void ApplyConfirmed(ItemSlot[] inventory, ItemMoveDirection direction, int from, int to, int countable, ushort amount = WholeStack)
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

    public static ItemMoveDirection DirectionFor(Region from, Region to) => (from, to) switch
    {
        (Region.Grid, Region.Equip) => ItemMoveDirection.InventoryToSlot,
        (Region.Equip, Region.Grid) => ItemMoveDirection.SlotToInventory,
        (Region.Grid, Region.Grid) => ItemMoveDirection.InventoryToInventory,
        (Region.Equip, Region.Equip) => ItemMoveDirection.SlotToSlot,
        (Region.Grid, Region.Cospre) => ItemMoveDirection.InventoryToCospre,
        (Region.Cospre, Region.Grid) => ItemMoveDirection.CospreToInventory,
        (Region.Grid, Region.BagSlot) => ItemMoveDirection.InventoryToBagSlot,
        (Region.BagSlot, Region.Grid) => ItemMoveDirection.BagSlotToInventory,
        (Region.Grid, Region.MagicBag) => ItemMoveDirection.InventoryToMagicBag,
        (Region.MagicBag, Region.Grid) => ItemMoveDirection.MagicBagToInventory,
        (Region.MagicBag, Region.MagicBag) => ItemMoveDirection.MagicBagToMagicBag,
        _ => ItemMoveDirection.None,
    };

    public static bool Merges(ItemMoveDirection direction, ItemSlot source, ItemSlot destination, int countable) =>
        direction is ItemMoveDirection.InventoryToInventory or ItemMoveDirection.InventoryToMagicBag or ItemMoveDirection.MagicBagToInventory or ItemMoveDirection.MagicBagToMagicBag
        && countable > 0
        && !source.IsEmpty
        && destination.ItemId == source.ItemId
        && destination.Flag == source.Flag
        && !source.IsLinked
        && !destination.IsLinked
        && source.Count + destination.Count <= Inventory.StackMax;

    public static bool SplitsAcross(ItemMoveDirection direction) =>
        direction is ItemMoveDirection.InventoryToInventory or ItemMoveDirection.InventoryToMagicBag or ItemMoveDirection.MagicBagToInventory or ItemMoveDirection.MagicBagToMagicBag;

    public static bool Splits(ItemMoveDirection direction, ItemSlot source, ItemSlot destination, int countable, int amount) =>
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

public sealed class StackDial
{
    public const int Up = 1;
    public const int Down = -1;
    public const double StreakPauseSeconds = 0.35;
    public const int NotchesPerStep = 4;
    private static readonly int[] Steps = { 1, 5, 10, 50, 100 };

    private int direction;
    private int streak;
    private double lastNotchAt;

    public StackDial(int total)
    {
        Total = Math.Max(1, total);
        Amount = Total;
    }

    public int Total { get; }
    public int Amount { get; private set; }
    public bool Whole => Amount == Total;

    public static bool Applies(int countable, int count) => countable > 0 && count > 1;

    public static int StepFor(int streak) => Steps[Math.Clamp((streak - 1) / NotchesPerStep, 0, Steps.Length - 1)];

    public int Turn(int direction, double now)
    {
        if (direction == 0 || Total == 1) return Amount;
        direction = Math.Sign(direction);
        bool quick = direction == this.direction && now - lastNotchAt <= StreakPauseSeconds;
        streak = quick ? streak + 1 : 1;
        this.direction = direction;
        lastNotchAt = now;
        int step = StepFor(streak);
        if (direction == Up)
        {
            if (Amount == Total) return Wrap(1);
            return Amount = Math.Min(Total, (Amount / step + 1) * step);
        }
        if (Amount == 1) return Wrap(Total);
        return Amount = Math.Max(1, ((Amount + step - 1) / step - 1) * step);
    }

    private int Wrap(int amount)
    {
        streak = 1;
        return Amount = amount;
    }
}
