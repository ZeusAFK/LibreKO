using Godot;
using LibreKO.Network.Readers;

namespace LibreKO.Network;

public partial class Net : Node, IGameConnection, IReconnectHost, IPreGameHost
{
    private const double PingIntervalSeconds = 1.0;
    private const ulong PingTimeoutMs = 5000;
    private const int MissedPingsBeforeDrop = 3;
    private const string WorldScene = "res://scenes/World.tscn";
    private const string LoginScene = "res://scenes/Login.tscn";

    public static Net I { get; private set; } = null!;

    private readonly GameConn conn = new();
    private readonly PacketDispatcher dispatcher = new();
    private readonly LoginState login = new();
    private readonly ReconnectSupervisor reconnect;
    private bool connectedFired;
    private bool connectFailReported;
    private bool expectedClose;
    private double pingAccum;
    private ulong pingSentMs;
    private bool pingOutstanding;
    private int missedPings;
    private int connGeneration;
    private string host = "";
    private int port;
    private bool zoneChanging;
    private bool rebuildingInPlace;

    public Net()
    {
        reconnect = new ReconnectSupervisor(this);
    }

    private GameSession Session => Game.Session;

    private GameRequests Requests => Game.Requests;

    public event Action? ConnectedEvent;

    public event Action<string>? ErrorEvent;

    public event Action<int>? VersionEvent;

    public int ServerVersion { get; private set; }

    public int PingMs { get; private set; } = -1;

    public bool Connected => conn.Connected;

    public string ServerHost => host;

    public ReconnectSupervisor Reconnect => reconnect;

    public bool WorldActive { get; set; }

    public Func<bool>? WindowCloseHandler { get; set; }

    public override void _Ready()
    {
        I = this;
        var session = new GameSession();
        Game.Boot(session, new GameRequests(conn, session, login), this);
        PacketReaders.Register(dispatcher, session, Game.Requests, Time.GetTicksMsec);
        new PreGameReader(session, Game.Requests.PreGame, login, reconnect, this).Register(dispatcher);
        dispatcher.On(GameOpcodes.GS_PING, HandlePing);
        dispatcher.On(GameOpcodes.GS_ZONE_CHANGE, HandleZoneChange);
        Config.Load();
        GetTree().AutoAcceptQuit = false;
    }

    public void BeginGameLogin(string host, int port, string account, string password)
    {
        login.Remember(account, password);
        login.AutoLoginPending = true;
        ConnectToServer(host, port);
    }

    public void ConnectToServer(string host, int port)
    {
        this.host = host;
        this.port = port;
        connectedFired = false;
        expectedClose = false;
        Session.AccountLock.ApplyEvicted(false);
        reconnect.AutoReconnect = true;
        reconnect.KickSent = false;
        Session.ResetPendingOperations();
        Session.Self.ApplyCharacterId(0);
        Session.Combat.ForgetSkillBar();
        PingMs = -1;
        pingOutstanding = false;
        pingAccum = 0;
        missedPings = 0;
        connectFailReported = false;
        reconnect.Cancel();
        connGeneration++;
        conn.Connect(host, port);
    }

    public void Disconnect(bool expected = false)
    {
        reconnect.Cancel();
        expectedClose = expected;
        SendLogout();
        Session.ResetPendingOperations();
        conn.Close();
        PingMs = -1;
        pingOutstanding = false;
    }

    public void ReturnToCharSelect()
    {
        reconnect.Cancel();
        SendLogout();
        Session.ResetPendingOperations();
        Session.Self.ApplyCharacterId(0);
        Session.Combat.ForgetSkillBar();
    }

    public void DropConnection(string reason)
    {
        missedPings = 0;
        conn.LastError = reason;
        conn.Close();
    }

    public void Receive(Packet packet) => Handle(packet);

    public void AcknowledgeWorldLoaded()
    {
        if (ConsumeZoneChange()) Requests.Zone.EnterAck();
        else if (!ConsumeRebuildInPlace()) Requests.PreGame.Ready();
    }

    public void RebuildWorldInPlace(float x, float z, float y)
    {
        Session.Self.MoveTo(x, z, y);
        rebuildingInPlace = true;
        GetTree().ChangeSceneToFile(WorldScene);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && conn.Connected && !reconnect.Blocking
            && WindowCloseHandler?.Invoke() == true)
            return;
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
            Disconnect(expected: true);
        if (what == NotificationWMCloseRequest)
        {
            try { Game.LoginServer?.Disconnect(); } catch { }
            OS.Kill(OS.GetProcessId());
        }
    }

    public override void _Process(double delta)
    {
        using var scope = Perf.Measure(Perf.Section.Net);
        if (conn.Connected && !connectedFired)
        {
            connectedFired = true;
            ConnectedEvent?.Invoke();
            Requests.PreGame.VersionCheck();
        }
        bool dropped = !conn.Connected && connectedFired;
        DrainIncoming(drainAll: dropped);
        if (dropped && connectedFired)
        {
            conn.Close();
            Session.ResetPendingOperations();
            connectedFired = false;
            PingMs = -1;
            pingOutstanding = false;
            if (!expectedClose && !Session.AccountLock.Evicted && !reconnect.TakeOverDisconnect())
                ErrorEvent?.Invoke(conn.LastError ?? "disconnected");
            expectedClose = false;
        }
        if (conn.ConnectFailed && !connectFailReported)
        {
            connectFailReported = true;
            if (!reconnect.TakeOverDisconnect())
                ErrorEvent?.Invoke(conn.LastError ?? "connection failed");
        }

        SchedulePing(delta);
        reconnect.Tick(delta);
    }

    internal void Handle(Packet p) => dispatcher.Dispatch(p);

    private void DrainIncoming(bool drainAll)
    {
        int backlog = conn.Incoming.Count;
        if (!drainAll && backlog > BacklogReconnectAbove)
        {
            DropConnection("the client fell too far behind the server");
            return;
        }
        double budgetMs = (Platform.Android ? PhoneDrainBudgetMs : DrainBudgetMs) * Math.Min(MaxBudgetScale, 1 + backlog / BacklogPerBudgetStep);
        long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
        while (conn.Incoming.TryDequeue(out var p))
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int size = p.RemainingBytes;
            try { Handle(p); }
            catch (Exception e) { ReportPacketError(p.GetOpcode(), e); }
            Diag.Hitch($"packet 0x{p.GetOpcode():X2} ({size} bytes)", started);
            if (!drainAll && System.Diagnostics.Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds >= budgetMs) break;
        }
    }

    private const double DrainBudgetMs = 16.0;
    private const double PhoneDrainBudgetMs = 6.0;
    private const int BacklogPerBudgetStep = 1000;
    private const int MaxBudgetScale = 4;
    private const int BacklogReconnectAbove = 15000;
    private const double PacketReportEverySeconds = 60.0;
    private readonly Dictionary<byte, double> packetReportedAt = new();

    private void ReportPacketError(byte opcode, Exception e)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (packetReportedAt.TryGetValue(opcode, out double last) && now - last < PacketReportEverySeconds) return;
        packetReportedAt[opcode] = now;
        Diag.Report($"packet 0x{opcode:X2}", e);
    }

    private void SendLogout()
    {
        if (!conn.Connected || Session.Self.MyCharId == 0) return;
        var logout = new Packet(GameOpcodes.GS_LOGOUT);
        logout.WriteByte(0);
        conn.Send(logout);
    }

    private void SchedulePing(double delta)
    {
        if (!conn.Connected || !Config.PingEnabled)
            return;

        ulong now = Time.GetTicksMsec();
        if (pingOutstanding && now - pingSentMs > PingTimeoutMs)
        {
            pingOutstanding = false;
            PingMs = -1;
            PingLost();
        }

        pingAccum += delta;
        if (pingAccum >= PingIntervalSeconds && !pingOutstanding)
        {
            pingAccum = 0;
            pingSentMs = now;
            pingOutstanding = true;
            var ping = new Packet(GameOpcodes.GS_PING);
            ping.WriteLong((long)Time.GetTicksMsec());
            conn.Send(ping);
        }
    }

    private void HandlePing(Packet p)
    {
        if (p.RemainingBytes >= sizeof(long))
        {
            PingMs = (int)((long)Time.GetTicksMsec() - p.ReadLong());
            pingOutstanding = false;
            missedPings = 0;
        }
    }

    private void PingLost()
    {
        if (++missedPings < MissedPingsBeforeDrop) return;
        DropConnection("the server stopped responding");
    }

    public const string ConnectionLostText = "The connection to the server was lost.";

    public void LeaveToLogin(string message)
    {
        Session.AccountLock.ApplyEvicted(true);
        reconnect.AutoReconnect = false;
        reconnect.Cancel();
        Session.Self.ApplyCharacterId(0);
        Disconnect(expected: true);
        LibreKO.Login.PendingNotice = message;
        GetTree().ChangeSceneToFile(LoginScene);
    }

    private bool ConsumeZoneChange()
    {
        var v = zoneChanging;
        zoneChanging = false;
        return v;
    }

    private bool ConsumeRebuildInPlace()
    {
        var v = rebuildingInPlace;
        rebuildingInPlace = false;
        return v;
    }

    private void HandleZoneChange(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (ZoneChangeSubOpcode)p.ReadByte();
        if (sub == ZoneChangeSubOpcode.Teleport && p.RemainingBytes >= 10)
        {
            int zoneId = p.ReadShort();
            p.ReadShort();
            float x = p.ReadUShort() / Coord.TenthsPerUnit;
            float z = p.ReadUShort() / Coord.TenthsPerUnit;
            float y = p.ReadUShort() / Coord.TenthsPerUnit;
            if (zoneId != Session.Self.LastEnter.Zone) Session.Chaos.ResetBattle();
            Session.Self.EnterZone(zoneId, x, z, y);
            Session.Entities.Clear();
            Session.UserOption.Clear();
            Session.Merchant.ForgetStalls();
            zoneChanging = true;
            GetTree().ChangeSceneToFile(WorldScene);
        }
        else if (sub == ZoneChangeSubOpcode.Ready)
        {
            Requests.Zone.Finish();
        }
    }

    bool IReconnectHost.CanResume =>
        Session.Self.MyCharId != 0 && host.Length > 0 && login.Account.Length > 0 && Session.Self.SelectedChar.Length > 0;

    string? IReconnectHost.LastError => conn.LastError;

    void IReconnectHost.StartAttempt()
    {
        connectedFired = false;
        connectFailReported = false;
        expectedClose = false;
        login.AutoLoginPending = true;
        login.SelectRetries = 0;
        pingOutstanding = false;
        pingAccum = 0;
        missedPings = 0;
        PingMs = -1;
        connGeneration++;
        conn.Connect(host, port);
    }

    void IReconnectHost.CloseConnection()
    {
        conn.Close();
        connectedFired = false;
    }

    void IReconnectHost.SelectCharacter() => Requests.PreGame.SelectCharacter(Session.Self.SelectedChar);

    void IReconnectHost.ReloadWorld() => GetTree().ChangeSceneToFile(WorldScene);

    void IPreGameHost.RecordVersion(int version) => ServerVersion = version;

    void IPreGameHost.AnnounceVersion() => VersionEvent?.Invoke(ServerVersion);

    void IPreGameHost.ReportError(string message) => ErrorEvent?.Invoke(message);

    void IPreGameHost.LeaveToLogin(string message) => LeaveToLogin(message);

    void IPreGameHost.RetryWhileConnected(double seconds, Action retry)
    {
        int generation = connGeneration;
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (generation == connGeneration && conn.Connected) retry();
        };
    }

    ulong IPreGameHost.NowMsec => Time.GetTicksMsec();

    void IPreGameHost.Warn(string message) => GD.PushWarning(message);

    bool IPreGameHost.RefreshesWorld => reconnect.State == ReconnectPhase.Idle && (WorldActive || rebuildingInPlace);
}
