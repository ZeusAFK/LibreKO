namespace LibreKO.Game.Protocol;

public enum ItemTradeResult : byte
{
    Refused = 0,
    Traded = 1,
    Moved = 3,
}

public enum ItemTradeRefusal : byte
{
    None = 0,
    CannotTrade = 2,
    NotEnoughCoins = 3,
    InventoryFull = 4,
}
