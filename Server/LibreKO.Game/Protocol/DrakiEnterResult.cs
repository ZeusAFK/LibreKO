namespace LibreKO.Game.Protocol;

public enum DrakiEnterResult : uint
{
    Success = 0,
    CannotEnter = 1,
    AlreadyInEvent = 2,
    InvalidItem = 5,
    Failed = 6,
    NoLimit = 7,
    Dead = 9,
}
