namespace LibreKO.Network;

public enum MiningSubOpcode : byte
{
    MiningStart = 1,
    MiningAttempt = 2,
    MiningStop = 3,
    BettingGame = 5,
    FishingStart = 6,
    FishingAttempt = 7,
    FishingStop = 8,
    Soccer = 16,
}

public enum MiningResult : ushort
{
    Error = 0,
    Success = 1,
    Already = 2,
    NotArea = 3,
    Preparing = 4,
    NoTool = 5,
    NothingFound = 6,
    NoEarthworm = 7,
}

public enum YutResult : ushort
{
    NotEnoughCoins = 0,
    Won = 1,
    Tied = 2,
    Lost = 3,
    NoCoins = 4,
}

public enum YutBetKind : byte
{
    Coins = 1,
    Item = 2,
}

public readonly record struct YutRoll(YutResult Result, int CharId, int PlayerRoll, int NpcRoll);
