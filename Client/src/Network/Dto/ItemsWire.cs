namespace LibreKO.Network;

public enum ItemMoveSubOpcode : byte
{
    Move = 1,
    ArrangeInventory = 2,
}

public enum ItemCountChangeKind : byte
{
    None = 0,
    Update = 1,
    Received = 2,
    ReceivedAlternate = 7,
}

public enum GoldChangeKind : byte
{
    Earned = 1,
    Lost = 2,
    Used = 3,
    Received = 4,
    Multiplied = 5,
}

public enum ExperienceSubOpcode : byte
{
    ExpRateAward = 2,
    CurrentExperience = 4,
    DeathExperienceLoss = 5,
}

public enum ItemRemoveReason : byte
{
    SystemError = 0,
    NoVipKeyPassword = 20,
    VipKeyLocked = 21,
    WrongVipKeyPassword = 22,
}

public enum ItemRemoveSource : byte
{
    Inventory = 0,
    Equipment = 1,
    Bag = 2,
    MagicBag = 3,
}

public enum ItemMoveDirection : byte
{
    None = 0,
    InventoryToSlot = 1,
    SlotToInventory = 2,
    InventoryToInventory = 3,
    SlotToSlot = 4,
    InventoryToZone = 5,
    ZoneToInventory = 6,
    InventoryToCospre = 7,
    CospreToInventory = 8,
    InventoryToMagicBag = 9,
    MagicBagToInventory = 10,
    MagicBagToMagicBag = 11,
    InventoryToPet = 12,
    PetToInventory = 13,
    InventoryToBagSlot = 14,
    BagSlotToInventory = 15,
}

public readonly record struct PointChangeReply(int Type, int Value, int MaxHp, int MaxMp, int TotalHit, int MaxWeight);
