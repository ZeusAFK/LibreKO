namespace LibreKO.Network;

public enum RepurchaseSubOpcode : byte
{
    Open = 1,
    Buy = 2,
    Close = 3,
    Refresh = 4,
}

public enum RepurchaseResult : byte
{
    Succeeded = 1,
    Failed = 2,
}

public enum RepurchaseError : short
{
    NoCoins = -8,
    Expired = -6,
    NoRoom = -4,
    GenericError = -1,
}

public enum ItemTradeResult : byte
{
    Refused = 0,
    Traded = 1,
    Moved = 3,
    Repurchase = 5,
}
