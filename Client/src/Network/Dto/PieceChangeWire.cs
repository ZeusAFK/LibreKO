namespace LibreKO.Network;

public enum BifrostResult : byte
{
    Failed = 0,
    Succeeded = 1,
    Rejected = 2,
    NoPiece = 4,
}

public enum BifrostRarity : byte
{
    None = 0,
    Red = 1,
    Green = 2,
    White = 3,
}

public readonly record struct PieceExchangeResult(
    BifrostResult ResultCode,
    int RewardItemId,
    int RewardPosition,
    int PieceItemId,
    int PiecePosition,
    byte Effect);
