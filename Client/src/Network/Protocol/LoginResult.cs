namespace LibreKO.Network;

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

public static class LoginResults
{
    public static string Describe(int result) => (LoginResult)result switch
    {
        LoginResult.IdNotFound => "No account is registered under that name.",
        LoginResult.InvalidPassword => "Wrong password. Try again.",
        LoginResult.AccountBlocked => "This account has been blocked.",
        LoginResult.AlreadyInGame => "This account is already in game.",
        LoginResult.EmailNotVerified => "This account has not been activated yet. Verify its email address first.",
        LoginResult.PrepaidCardEmpty => "There is no play time left on this account.",
        LoginResult.PrepaidCardExpired => "The play time on this account has expired.",
        LoginResult.PasswordResetRequired => "This password has to be reset on the web site before you can sign in.",
        _ => $"Sign-in failed (code {result}).",
    };
}
