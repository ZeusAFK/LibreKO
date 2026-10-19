namespace LibreKO.Network;

public enum MerchantSearchSubOpcode : byte
{
    Move = 2,
    Open = 5,
    Results = 6,
}

public enum MerchantSearchResult : byte
{
    Refused = 0,
    Accepted = 1,
    MoreToCome = 2,
    LastChunk = 3,
}

public static class MerchantSearchWire
{
    public const int CannotUseText = 30524;
    public const int MoveFailedText = 30526;
}
