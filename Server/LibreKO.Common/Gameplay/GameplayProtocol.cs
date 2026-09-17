namespace LibreKO.Common.Gameplay;

public static class GameplayProtocol
{
    public const byte ExtensionVersion = 1;

    // ASCII "GSL1", "ALK1", "ACC1" and "DLG1" as little-endian uint values.
    public const uint ServerListMagic = 0x314C5347;
    public const uint AccountLockMagic = 0x314B4C41;
    public const uint AccountExtraMagic = 0x31434341;
    public const uint DialogTextMagic = 0x31474C44;
    public const uint DialogChoicesMagic = 0x32474C44;
}

public enum AccountClaimStage : byte
{
    PreGame = 0,
    InGame = 1,
}

public enum GameLoginDenial : byte
{
    AccountInUse = 1,
}

public enum AccountKickCode : byte
{
    Evicted = 0,
    Done = 1,
    NotOnline = 2,
    Rejected = 3,
}

[Flags]
public enum LoginRequestFlags : byte
{
    None = 0,
    IgnoreOnlineClaim = 1,
}
