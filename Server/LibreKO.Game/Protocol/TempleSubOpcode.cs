namespace LibreKO.Game.Protocol;

public enum TempleSubOpcode : byte
{
    BifrostRemaining = 2,
    TempleScreen = 3,
    MonsterSquad = 5,
    MonsterStone = 6,
    TempleEvent = 7,
    TempleEventJoin = 8,
    TempleEventDisband = 9,
    TempleEventFinish = 10,
    TempleEventCounter = 16,
    AltarKilledMessage = 49,
    DrakiEnter = 33,
    DrakiList = 34,
    DrakiTimer = 35,
    DrakiLeaveFirst = 36,
    DrakiLeaveSecond = 37,
    DrakiTown = 38,
}
