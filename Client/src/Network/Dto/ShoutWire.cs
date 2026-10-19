namespace LibreKO.Network;

public enum LogosShoutSubOpcode : byte
{
    RegisterResult = 1,
    Broadcast = 2,
}

public enum LogosShoutRegisterResult : sbyte
{
    LevelTooLow = -6,
    ChatRestricted = -5,
    RetryLater = -3,
    NoItem = -2,
    Accepted = 1,
}

public enum LogosShoutBroadcastKind : byte
{
    Message = 1,
    RareItem = 4,
    Upgrade = 5,
}

public static class ShoutWire
{
    public const int MaxLength = 128;
}

public readonly record struct ShoutReply(string Message, byte R, byte G, byte B, int Rank);
