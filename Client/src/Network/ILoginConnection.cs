namespace LibreKO.Network;

public interface ILoginConnection
{
    string Account { get; }

    event Action? ConnectedEvent;

    event Action<int>? VersionEvent;

    event Action<bool, int>? LoginResultEvent;

    event Action<GameLanguage>? AccountLanguageEvent;

    event Action<AccountInUse>? AccountInUseEvent;

    event Action<List<ServerListEntry>>? ServerListEvent;

    event Action<string>? ErrorEvent;

    void ConnectToLoginServer();

    bool ReconnectForServerSelection();

    void Disconnect(bool expected = false);

    void Login(string account, string password, LoginRequestFlags flags = LoginRequestFlags.None);

    void RetryLogin(LoginRequestFlags flags = LoginRequestFlags.None);

    void RequestServerList();

    (string account, string password) Credentials();
}
