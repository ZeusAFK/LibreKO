using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO.Network;

public partial class LoginNet : Node
{
    public static LoginNet I { get; private set; } = null!;

    public const char ServerGroupSeparator = '|';

    public sealed class ServerEntry
    {
        public string Name = "";
        public string Group = "";
        public string LanIp = "";
        public string WanIp = "";
        public short Players;
        public short Id;
        public short MaxPlayers;
        public int Port;

        public string Address => WanIp.Length > 0 ? WanIp : LanIp;
    }

    public event Action? ConnectedEvent;
    public event Action<int>? VersionEvent;
    public event Action<bool, int>? LoginResultEvent;
    public event Action<GameLanguage>? AccountLanguageEvent;
    public event Action<AccountInUse>? AccountInUseEvent;
    public event Action<List<ServerEntry>>? ServerListEvent;
    public event Action<string>? ErrorEvent;

    private readonly LoginConn _conn = new();
    private bool _connectedFired;
    private bool _connectFailReported;
    private bool _expectedClose;
    private bool _autoLoginPending;
    private short _echo;

    public string Account { get; private set; } = "";
    private string? _password;

    public override void _Ready()
    {
        I = this;
        Config.Load();
    }

    public void ConnectToLoginServer() => ConnectToLoginServer(autoLogin: false);

    private void ConnectToLoginServer(bool autoLogin)
    {
        _autoLoginPending = autoLogin;
        _connectedFired = false;
        _connectFailReported = false;
        _expectedClose = false;
        _conn.Connect(Config.ServerHost, Config.ServerPort);
    }

    public bool ReconnectForServerSelection()
    {
        if (Account.Length == 0 || _password == null)
            return false;

        ConnectToLoginServer(autoLogin: true);
        return true;
    }

    public void Disconnect(bool expected = false)
    {
        _expectedClose = expected;
        _conn.Close();
    }

    public void Login(string account, string password, LoginRequestFlags flags = LoginRequestFlags.None)
    {
        Account = account;
        _password = password;
        var p = new Packet(LoginOpcodes.LS_LOGIN);
        p.WriteString(account);
        p.WriteString(KoPassword.Encode(password));
        if (flags != LoginRequestFlags.None)
        {
            p.WriteUInt(LibreKOProtocol.AccountLockMagic);
            p.WriteByte(LibreKOProtocol.ExtensionVersion);
            p.WriteByte((byte)flags);
        }
        _conn.Send(p);
    }

    public void RetryLogin(LoginRequestFlags flags = LoginRequestFlags.None)
        => Login(Account, _password ?? "", flags);

    public void RequestServerList()
    {
        _echo = 1;
        var p = new Packet(LoginOpcodes.LS_SERVERLIST);
        p.WriteShort(_echo);
        _conn.Send(p);
    }

    public (string account, string password) Credentials() => (Account, _password ?? "");

    public override void _Process(double delta)
    {
        if (_conn.Connected && !_connectedFired)
        {
            _connectedFired = true;
            ConnectedEvent?.Invoke();
            SendCryptionHandshake();
        }
        if (!_conn.Connected && _connectedFired)
        {
            _connectedFired = false;
            if (!_expectedClose)
                ErrorEvent?.Invoke(_conn.LastError ?? "disconnected");
        }
        if (_conn.ConnectFailed && !_connectFailReported)
        {
            _connectFailReported = true;
            ErrorEvent?.Invoke(_conn.LastError ?? "connection failed");
        }
        while (_conn.Incoming.TryDequeue(out var p))
            Handle(p);
    }

    private void SendCryptionHandshake() => _conn.Send(new Packet(LoginOpcodes.LS_CRYPTION));

    private void SendVersionCheck() => _conn.Send(new Packet(LoginOpcodes.LS_VERSION_REQ));

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
                if (_autoLoginPending)
                {
                    _autoLoginPending = false;
                    Login(Account, _password ?? "");
                }
                break;
            }

            case LoginOpcodes.LS_LOGIN:
            {
                p.ReadShort();
                int result = p.ReadByte();
                if (result == (int)LoginResult.AlreadyInGame)
                {
                    AccountInUseEvent?.Invoke(ParseOccupiedServer(p));
                    break;
                }
                if (result == (int)LoginResult.Success)
                    ParseAccountExtras(p);
                LoginResultEvent?.Invoke(result == (int)LoginResult.Success, result);
                break;
            }

            case LoginOpcodes.LS_SERVERLIST:
                ParseServerList(p);
                break;
        }
    }

    private void ParseAccountExtras(Packet p)
    {
        try
        {
            p.ReadShort();
            p.ReadString();

            const int extensionHeaderBytes = 5;
            if (p.RemainingBytes < extensionHeaderBytes + 1
                || p.ReadUInt() != LibreKOProtocol.AccountExtraMagic
                || p.ReadByte() != LibreKOProtocol.ExtensionVersion)
                return;

            var language = LibreKOProtocol.ToLanguage(p.ReadByte());
            Config.SetLanguage(language);
            AccountLanguageEvent?.Invoke(language);
        }
        catch (Exception e)
        {
            Diag.Report("account extras parse", e);
        }
    }

    private static AccountInUse ParseOccupiedServer(Packet p)
    {
        var info = new AccountInUse { Stage = AccountClaimStage.InGame };
        try
        {
            info.Host = p.ReadString();
            info.Port = p.ReadUShort();
            info.ServerId = (int)p.ReadUInt();
            info.ServerName = p.ReadString();

            const int extensionHeaderBytes = 5;
            if (p.RemainingBytes >= extensionHeaderBytes
                && p.ReadUInt() == LibreKOProtocol.AccountLockMagic
                && p.ReadByte() == LibreKOProtocol.ExtensionVersion)
            {
                int port = p.ReadInt();
                if (port is >= 1 and <= 65535)
                    info.Port = port;
                string lanIp = p.ReadString();
                if (info.Host.Length == 0)
                    info.Host = lanIp;
            }
        }
        catch (Exception e)
        {
            Diag.Report("account-in-use parse", e);
        }

        if (info.Host.Length == 0) info.Host = Config.ServerHost;
        if (info.Port <= 0) info.Port = Config.GamePort;
        return info;
    }

    private void ParseServerList(Packet p)
    {
        p.ReadShort();
        int count = p.ReadByte();
        var list = new List<ServerEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var e = new ServerEntry
            {
                WanIp = p.ReadString(),
                LanIp = p.ReadString(),
                Port = Config.GamePort,
            };
            SplitServerName(p.ReadString(), e);
            e.Players = p.ReadShort();
            e.Id = p.ReadShort();
            p.ReadShort();
            e.MaxPlayers = p.ReadShort();
            p.ReadShort();
            p.ReadByte();
            p.ReadByte();
            p.ReadString();
            p.ReadString();
            p.ReadString();
            p.ReadString();
            list.Add(e);
        }

        if (p.RemainingBytes >= 6 && p.ReadUInt() == LibreKOProtocol.ServerListMagic)
        {
            byte extensionVersion = p.ReadByte();
            int extensionCount = p.ReadByte();
            if (extensionVersion == LibreKOProtocol.ExtensionVersion)
            {
                var byId = new Dictionary<short, ServerEntry>();
                foreach (var entry in list)
                    byId[entry.Id] = entry;

                for (int i = 0; i < extensionCount && p.RemainingBytes >= 6; i++)
                {
                    short serverId = p.ReadShort();
                    int port = p.ReadInt();
                    if (port is >= 1 and <= 65535 && byId.TryGetValue(serverId, out var entry))
                        entry.Port = port;
                }
            }
        }
        ServerListEvent?.Invoke(list);
    }

    private static void SplitServerName(string wireName, ServerEntry entry)
    {
        int pipe = wireName.IndexOf(ServerGroupSeparator);
        if (pipe < 0)
        {
            entry.Name = wireName;
            return;
        }
        entry.Group = wireName[..pipe];
        entry.Name = wireName[(pipe + 1)..];
    }
}
