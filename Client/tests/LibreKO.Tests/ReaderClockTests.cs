using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class ReaderClockTests
{
    private const ulong Now = 50_000;
    private const ushort NestFinishEvent = 17;

    private sealed class NoConnection : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    private static (PacketDispatcher Dispatcher, GameSession Session) Readers()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoConnection(), session, new LoginState()), () => Now);
        return (dispatcher, session);
    }

    private static Packet Incoming(GameOpcodes opcode, Action<Packet> body)
    {
        var p = new Packet(opcode);
        body(p);
        p.ResetOffset();
        return p;
    }

    private static Packet EventFinish(ushort eventId, uint seconds) => Incoming(GameOpcodes.GS_EVENT, p =>
    {
        p.WriteByte((byte)TempleSubOpcode.TempleEventFinish);
        p.WriteUShort(eventId);
        p.WriteByte(0);
        p.WriteUInt(seconds);
    });

    [Fact]
    public void TheDrakiTimerEndsWhatIsLeftOfItsLimitAfterTheGivenClock()
    {
        var (dispatcher, session) = Readers();

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_EVENT, p =>
        {
            p.WriteByte((byte)TempleSubOpcode.DrakiTimer);
            p.WriteUShort(0);
            p.WriteUShort(3);
            p.WriteUShort(2);
            p.WriteInt(600);
            p.WriteInt(100);
        }));

        Assert.Equal(3, session.Bifrost.DrakiStage);
        Assert.Equal(2, session.Bifrost.DrakiSubStage);
        Assert.Equal(Now + 500_000, session.Bifrost.DrakiEndsAtMs);
    }

    [Fact]
    public void TheNestTimerCountsFromTheGivenClock()
    {
        var (dispatcher, session) = Readers();

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_BIFROST, p =>
        {
            p.WriteByte((byte)TempleSubOpcode.MonsterSquad);
            p.WriteShort(90);
        }));

        Assert.False(session.MonsterStone.NestCompleted);
        Assert.Equal(Now + 90_000, session.MonsterStone.NestEndsAtMs);
    }

    [Fact]
    public void TheNestCompletionCountsDownToTheExit()
    {
        var (dispatcher, session) = Readers();

        dispatcher.Dispatch(EventFinish(NestFinishEvent, 30));

        Assert.True(session.MonsterStone.NestCompleted);
        Assert.Equal(Now + 30_000, session.MonsterStone.NestEndsAtMs);
    }

    [Fact]
    public void TheChaosFinishEndsOnTheGivenClock()
    {
        var (dispatcher, session) = Readers();

        dispatcher.Dispatch(EventFinish(ChaosBattle.IndunEvent, 20));

        Assert.Equal(Now + 20_000, session.Chaos.FinishEndsAtMs);
    }

    [Fact]
    public void TheRoyaleGasCountsDownFromTheGivenClock()
    {
        var (dispatcher, session) = Readers();

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_KNIGHT_ROYALE, p =>
        {
            p.WriteByte((byte)KnightRoyaleSubOpcode.Notice);
            p.WriteByte((byte)KnightRoyaleNoticeKind.Gas);
            p.WriteByte(0);
            p.WriteByte(2);
        }));

        Assert.Equal(120, session.KnightRoyale.NextGasSeconds(Now));
    }
}
