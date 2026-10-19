namespace LibreKO.Network;

public enum ItemUpgradeSubOpcode : byte
{
    AnvilOpen = 1,
    Upgrade = 2,
    UpgradeAccessories = 3,
    BifrostRequest = 4,
    BifrostExchange = 5,
    PetHatching = 6,
    UpgradeRebirth = 7,
    ItemSeal = 8,
    CharacterSeal = 9,
    PetTransform = 10,
    Combine = 11,
    Smash = 13,
}

public enum ItemUpgradeKind : byte
{
    Normal = 1,
    Preview = 2,
}

public enum ItemUpgradeResult : byte
{
    Failed = 0,
    Succeeded = 1,
    Trading = 2,
    NeedCoins = 3,
    NoMatch = 4,
    Rental = 5,
}

public static class ItemUpgradeWire
{
    public const int SlotCount = 10;
}

public readonly record struct UpgradeSlotResult(int ItemId, int Position);

public readonly record struct UpgradeResult(
    ItemUpgradeSubOpcode SubOpcode,
    ItemUpgradeKind UpgradeType,
    ItemUpgradeResult ResultCode,
    UpgradeSlotResult[] Slots);
