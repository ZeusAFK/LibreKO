namespace LibreKO.Network;

public enum BattleEventSubOpcode : byte
{
    Open = 1,
    Result = 3,
    RankBoard = 13,
    ClanMatch = 19,
}

public enum BattleEventState : byte
{
    None = 0,
    Nation = 1,
    Snow = 2,
}

public enum BattleEventNotice : byte
{
    ZoneOpen = 0x00,
    ZoneClose = 0x01,
    DeclareWinner = 0x02,
    DeclareLoser = 0x03,
    SnowZoneOpen = 0x09,
}
