namespace LibreKO.Network;

public enum TempleSubOpcode : byte
{
    BifrostRemaining = 2,
    TempleScreen = 3,
    MonsterSquad = 5,
    MonsterStone = 6,
    TempleEventJoin = 8,
    TempleEventDisband = 9,
    TempleEventFinish = 10,
    MonsterStoneExp = 11,
    TempleEventCounter = 16,
    DrakiEnter = 33,
    DrakiList = 34,
    DrakiTimer = 35,
    DrakiLeaveFirst = 36,
    DrakiLeaveSecond = 37,
    AltarKilledMessage = 49,
    AltarTimer = 50,
    FullMoonEntry = 58,
    FullMoonTower = 59,
    FullMoonStage = 60,
}

public enum TempleJoinResult : byte
{
    Joined = 0,
    AlreadyJoined = 1,
    WrongPoints = 2,
    NeedsMapOfChaos = 3,
    LadderPoints = 4,
    OtherDungeon = 5,
    LevelTooLow = 6,
    DuringWar = 7,
    DuplicateIp = 8,
    NotOpen = 9,
    ApplyLadderPoints = 10,
    LevelTooHigh = 11,
}
