namespace LibreKO.Network;

public enum EventBoardSubOpcode : byte
{
    AttendanceBoard = 4,
    AttendanceClaim = 5,
    RouletteOpen = 6,
    RouletteSpin = 8,
    RoulettePrizeList = 9,
}

public enum EventBoardResult : uint
{
    Failed = 0,
    Ok = 1,
    NotEnoughGold = 2,
    NotEnoughGoldForItem = 20,
    InventoryFull = 21,
    TooHeavy = 22,
    ItemNotObtained = 200,
}

public static class EventBoardWire
{
    public const uint Channel = 0;
}
