namespace LibreKO.Game.Protocol;

public enum BattleEventSubOpcode : byte
{
    Open = 1,
    MapEventResult = 2,
    Result = 3,
    MaxUser = 4,
    KillUser = 5,
    RankBoard = 13,
    ClanMatch = 19,
}
