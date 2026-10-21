namespace LibreKO.Game.Protocol;

public enum DrakiEnterResult : byte
{
    LevelTooLow = 0,
    WrongZone = 1,
    AlreadyInInstance = 2,
    FloorNotReached = 4,
    NoLimit = 5,
    RoomCreationFailed = 6,
    InvalidItem = 7,
    CannotEnterNow = 13,
}
