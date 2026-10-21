using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class PreGameReaderTests
{
    private const ushort ServerVersion = 2619;
    private const byte LoginRejected = byte.MaxValue;

    private sealed class RecordingSink : IPacketSink
    {
        public readonly List<GameOpcodes> Sent = [];

        public bool Connected => true;

        public void Send(Packet packet) => Sent.Add((GameOpcodes)packet.GetOpcode());
    }

    private sealed class Host : IPreGameHost, IReconnectHost
    {
        public int Version = -1;
        public bool Announced;
        public bool? DisconnectedExpected;
        public readonly List<string> Errors = [];

        public void RecordVersion(int version) => Version = version;
        public void AnnounceVersion() => Announced = true;
        public void Disconnect(bool expected) => DisconnectedExpected = expected;
        public void ReportError(string message) => Errors.Add(message);
        public void LeaveToLogin(string message) { }
        public void RetryWhileConnected(double seconds, Action retry) { }
        public bool RefreshesWorld => false;
        public void RebuildWorldInPlace(float x, float z, float y) { }
        public ulong NowMsec => 0;
        public void Warn(string message) { }

        public bool CanResume => false;
        public string? LastError => null;
        public void StartAttempt() { }
        public void CloseConnection() { }
        public void SelectCharacter() { }
        public void ReloadWorld() { }
    }

    private static (PacketDispatcher Dispatcher, RecordingSink Sink, Host Host, LoginState Login) Reader()
    {
        var session = new GameSession();
        var sink = new RecordingSink();
        var host = new Host();
        var login = new LoginState();
        var dispatcher = new PacketDispatcher();
        var requests = new GameRequests(sink, session, login);
        new PreGameReader(session, requests.PreGame, login, new ReconnectSupervisor(host), host).Register(dispatcher);
        return (dispatcher, sink, host, login);
    }

    private static Packet VersionCheck(GameServerState? state)
    {
        var p = new Packet(GameOpcodes.GS_VERSION_CHECK);
        p.WriteByte(0);
        p.WriteUShort(ServerVersion);
        if (state is { } value)
        {
            p.WriteByte(0);
            p.WriteByte((byte)value);
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void CurrentVersionSendsThePendingLogin()
    {
        var (dispatcher, sink, host, login) = Reader();
        login.Remember("zeus", "test");
        login.AutoLoginPending = true;

        dispatcher.Dispatch(VersionCheck(GameServerState.Open));

        Assert.Equal(ServerVersion, host.Version);
        Assert.True(host.Announced);
        Assert.False(login.AutoLoginPending);
        Assert.Equal([GameOpcodes.GS_LOGIN], sink.Sent);
    }

    [Fact]
    public void OutOfDateClientDisconnectsWithoutLoggingIn()
    {
        var (dispatcher, sink, host, login) = Reader();
        login.Remember("zeus", "test");
        login.AutoLoginPending = true;

        dispatcher.Dispatch(VersionCheck(GameServerState.ClientOutOfDate));

        Assert.False(host.Announced);
        Assert.True(host.DisconnectedExpected);
        Assert.Single(host.Errors);
        Assert.Empty(sink.Sent);
        Assert.False(login.AutoLoginPending);
    }

    [Fact]
    public void SelectingACharacterResetsTheRetries()
    {
        var (_, sink, _, login) = Reader();
        var requests = new PreGameRequests(sink, new SelfSession(), login);
        login.SelectRetries = 2;

        requests.SelectCharacter("Zeus");

        Assert.Equal(0, login.SelectRetries);
        Assert.Equal([GameOpcodes.GS_SELECT_CHARACTER], sink.Sent);
    }

    [Fact]
    public void ARenamedCharacterReachesThePreGameSession()
    {
        var session = new GameSession();
        var sink = new RecordingSink();
        var host = new Host();
        var login = new LoginState();
        var dispatcher = new PacketDispatcher();
        new PreGameReader(session, new GameRequests(sink, session, login).PreGame, login, new ReconnectSupervisor(host), host).Register(dispatcher);
        (SelectingCharacterNameChangeResult, int, string)? renamed = null;
        session.PreGame.RequiredNameChangeEvent += (result, slot, name) => renamed = (result, slot, name);
        var p = new Packet(GameOpcodes.GS_ALLCHAR_INFO_REQ);
        p.WriteByte((byte)AllCharacterInfoOpcode.NameChange);
        p.WriteByte((byte)SelectingCharacterNameChangeResult.Success);
        p.WriteShort(2);
        p.WriteString("Zeus");
        p.ResetOffset();

        dispatcher.Dispatch(p);

        Assert.Equal((SelectingCharacterNameChangeResult.Success, 2, "Zeus"), renamed);
    }

    [Fact]
    public void AThrottledGameLoginIsToldApartFromAFailedOne()
    {
        var session = new GameSession();
        var login = new LoginState();
        var dispatcher = new PacketDispatcher();
        new PreGameReader(session, new GameRequests(new RecordingSink(), session, login).PreGame, login, new ReconnectSupervisor(new Host()), new Host())
            .Register(dispatcher);
        var throttled = 0;
        var failed = 0;
        session.PreGame.LoginThrottledEvent += () => throttled++;
        session.PreGame.LoginResultEvent += (ok, _) => failed += ok ? 0 : 1;

        dispatcher.Dispatch(Denied(GameLoginDenial.TooManyAttempts));
        dispatcher.Dispatch(Denied(null));

        Assert.Equal((1, 1), (throttled, failed));
    }

    private static Packet Denied(GameLoginDenial? denial)
    {
        var p = new Packet(GameOpcodes.GS_LOGIN);
        p.WriteByte(LoginRejected);
        if (denial is { } reason)
        {
            p.WriteUInt(LibreKOProtocol.AccountLockMagic);
            p.WriteByte(LibreKOProtocol.ExtensionVersion);
            p.WriteByte((byte)reason);
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void ARequiredNameChangeSendsTheSlotAndBothNames()
    {
        var (_, sink, _, login) = Reader();
        var requests = new PreGameRequests(sink, new SelfSession(), login);

        requests.ChangeRequiredName(1, "Old", "New");

        Assert.Equal([GameOpcodes.GS_ALLCHAR_INFO_REQ], sink.Sent);
    }
}
