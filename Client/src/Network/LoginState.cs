namespace LibreKO.Network;

internal sealed class LoginState
{
    public string Account { get; private set; } = "";

    public string Password { get; private set; } = "";

    public bool AutoLoginPending { get; set; }

    public int SelectRetries { get; set; }

    public void Remember(string account, string password)
    {
        Account = account;
        Password = password;
    }
}
