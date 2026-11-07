namespace LibreKO.Game.Protocol;

public enum EventBoardSubOpcode : byte
{
    PostBoard = 2,
    PostRegister = 3,
    AttendanceBoard = 4,
    AttendanceClaim = 5,
    RouletteOpen = 6,
    RouletteAck = 7,
    RouletteSpin = 8,
    RoulettePrizeList = 9,
    PostHistory = 15,
}
