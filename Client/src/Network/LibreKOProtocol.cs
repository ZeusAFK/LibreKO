using System;

namespace LibreKO.Network;

public static class LibreKOProtocol
{
    public const byte ExtensionVersion = 1;

    public const uint ServerListMagic = 0x314C5347;
    public const uint AccountLockMagic = 0x314B4C41;
    public const uint AccountExtraMagic = 0x31434341;
    public const uint DialogTextMagic = 0x31474C44;
    public const uint DialogChoicesMagic = 0x32474C44;

    public static GameLanguage ToLanguage(byte raw) =>
        Enum.IsDefined(typeof(GameLanguage), raw) ? (GameLanguage)raw : GameLanguage.English;
}

public enum GameLanguage : byte
{
    English = 0,
    Spanish = 1,
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

public enum LoginRequestFlags : byte
{
    None = 0,
    IgnoreOnlineClaim = 1,
}

public sealed class AccountInUse
{
    public int ServerId;
    public string ServerName = "";
    public string Host = "";
    public int Port;
    public string CharacterName = "";
    public AccountClaimStage Stage;

    public string Where => ServerName.Length > 0 ? ServerName : "another server";

    public string Describe() => Stage == AccountClaimStage.InGame && CharacterName.Length > 0
        ? $"This account is already playing as {CharacterName} on {Where}."
        : $"This account is already connected to {Where}.";
}
