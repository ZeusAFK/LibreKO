namespace LibreKO.Network;

public enum ItemGetResult : byte
{
    Failed = 0,
    Taken = 1,
    PartyCoins = 2,
    PartyMember = 3,
    TakenToSlot = 5,
    TemporaryKept = 7,
    TemporaryLost = 8,
}

public static class LootWire
{
    public const int GoldItemId = 900000000;
    public const int MaxItems = 8;
    public const int WireSlots = 12;
    public const float Range = 11f;
}
