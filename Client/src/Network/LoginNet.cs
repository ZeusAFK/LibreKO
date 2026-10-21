using Godot;

namespace LibreKO.Network;

public partial class LoginNet : Node, ILoginConnection
{
    public event Action? ConnectedEvent;
    public event Action<int>? VersionEvent;
    public event Action<bool, int>? LoginResultEvent;
    public event Action<GameLanguage>? AccountLanguageEvent;
    public event Action<AccountInUse>? AccountInUseEvent;
    public event Action<List<ServerListEntry>>? ServerListEvent;
    public event Action<string>? ErrorEvent;

    private readonly LoginConn conn = new();
    private bool connectedFired;
    private bool connectFailReported;
    private bool expectedClose;
    private bool autoLoginPending;
    private short echo;

    public string Account { get; private set; } = "";
    private string? password;

    public override void _Ready()
    {
        Game.AttachLoginServer(this);
        Config.Load();
    }

    public void ConnectToLoginServer() => ConnectToLoginServer(autoLogin: false);

    private void ConnectToLoginServer(bool autoLogin)
    {
        autoLoginPending = autoLogin;
        connectedFired = false;
        connectFailReported = false;
        expectedClose = false;
        conn.Connect(Config.ServerHost, Config.ServerPort);
    }

    public bool ReconnectForServerSelection()
    {
        if (Account.Length == 0 || password == null)
            return false;

        ConnectToLoginServer(autoLogin: true);
        return true;
    }

    public void Disconnect(bool expected = false)
    {
        expectedClose = expected;
        conn.Close();
    }

    public void Login(string account, string password, LoginRequestFlags flags = LoginRequestFlags.None)
    {
        Account = account;
        this.password = password;
        var p = new Packet(LoginOpcodes.LS_LOGIN);
        p.WriteString(account);
        p.WriteString(LoginPassword.Encode(password));
        if (flags != LoginRequestFlags.None)
        {
            p.WriteUInt(LibreKOProtocol.AccountLockMagic);
            p.WriteByte(LibreKOProtocol.ExtensionVersion);
            p.WriteByte((byte)flags);
        }
        conn.Send(p);
    }

    public void RetryLogin(LoginRequestFlags flags = LoginRequestFlags.None)
        => Login(Account, password ?? "", flags);

    public void RequestServerList()
    {
        echo = 1;
        var p = new Packet(LoginOpcodes.LS_SERVERLIST);
        p.WriteShort(echo);
        conn.Send(p);
    }

    public (string account, string password) Credentials() => (Account, password ?? "");

    public override void _Process(double delta)
    {
        if (conn.Connected && !connectedFired)
        {
            connectedFired = true;
            ConnectedEvent?.Invoke();
            SendCryptionHandshake();
        }
        if (!conn.Connected && connectedFired)
        {
            connectedFired = false;
            if (!expectedClose)
                ErrorEvent?.Invoke(conn.LastError ?? "disconnected");
        }
        if (conn.ConnectFailed && !connectFailReported)
        {
            connectFailReported = true;
            ErrorEvent?.Invoke(conn.LastError ?? "connection failed");
        }
        while (conn.Incoming.TryDequeue(out var p))
        {
            try
            {
                Handle(p);
            }
            catch (Exception e)
            {
                Diag.Report($"login packet 0x{p.GetOpcode():X2}", e);
                ErrorEvent?.Invoke(UnreadableReplyText);
            }
        }
    }

    private const string UnreadableReplyText = "the login server sent a reply this client cannot read";

    private void SendCryptionHandshake() => conn.Send(new Packet(LoginOpcodes.LS_CRYPTION));

    private void SendVersionCheck() => conn.Send(new Packet(LoginOpcodes.LS_VERSION_REQ));

    private void Handle(Packet p)
    {
        switch ((LoginOpcodes)p.GetOpcode())
        {
            case LoginOpcodes.LS_CRYPTION:
                SendVersionCheck();
                break;

            case LoginOpcodes.LS_VERSION_REQ:
            {
                int version = p.ReadShort();
                VersionEvent?.Invoke(version);
                if (autoLoginPending)
                {
                    autoLoginPending = false;
                    Login(Account, password ?? "");
                }
                break;
            }

            case LoginOpcodes.LS_LOGIN:
            {
                p.ReadShort();
                int result = p.ReadByte();
                if (result == (int)LoginResult.AlreadyInGame)
                {
                    AccountInUseEvent?.Invoke(LoginWire.ReadOccupiedServer(p, Config.ServerHost, Config.GamePort));
                    break;
                }
                if (result == (int)LoginResult.Success)
                    ParseAccountExtras(p);
                LoginResultEvent?.Invoke(result == (int)LoginResult.Success, result);
                break;
            }

            case LoginOpcodes.LS_SERVERLIST:
                ServerListEvent?.Invoke(LoginWire.ReadServerList(p, Config.GamePort));
                break;
        }
    }

    private void ParseAccountExtras(Packet p)
    {
        try
        {
            if (LoginWire.ReadAccountLanguage(p) is not { } language) return;
            Config.SetLanguage(language);
            AccountLanguageEvent?.Invoke(language);
        }
        catch (Exception e)
        {
            Diag.Report("account extras parse", e);
        }
    }
}
