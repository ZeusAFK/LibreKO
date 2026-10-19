namespace LibreKO.Network;

public enum WarpListSubOpcode : byte
{
    Menu = 1,
    Result = 2,
}

public enum WarpResult : byte
{
    Arrived = 1,
    LevelTooLow = 2,
    CastleSiege = 3,
    LunarWar = 4,
    NoNationalPoints = 5,
    LevelRangeOnly = 6,
    NotQualified = 7,
    TradeCooldown = 8,
    ServerFull = 9,
    QuestCompleted = 10,
    ClanGrade = 12,
    SameAddress = 13,
    Captcha = 14,
}
