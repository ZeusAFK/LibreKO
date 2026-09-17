namespace LibreKO.Login.Enums;

public enum LoginResult : short
{
    Success = 0x01,
    IdNotFound = 0x02,
    InvalidPassword = 0x03,
    AccountBlocked = 0x04,
    AlreadyInGame = 0x05,
    EmailNotVerified = 0x06,
    PrepaidCardEmpty = 0x0B,
    PrepaidCardExpired = 0x0C,
    PasswordResetRequired = 0x11,
}
