using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class MagicExpiryReaderTests
{
    private const int TransformSkill = 500516;
    private const int CharId = 4321;
    private const int DataInts = 7;
    private const byte StunBuffType = 13;

    private sealed class NoConnection : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    private static (PacketDispatcher Dispatcher, GameSession Session) Readers()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoConnection(), session, new LoginState()), () => 0);
        return (dispatcher, session);
    }

    private static Packet LongForm()
    {
        var p = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        p.WriteByte(MagicSub.DurationExpired);
        p.WriteInt(TransformSkill);
        p.WriteInt(CharId);
        p.WriteInt(CharId);
        for (int i = 0; i < DataInts; i++) p.WriteInt(0);
        p.ResetOffset();
        return p;
    }

    private static Packet ShortForm(byte buffType)
    {
        var p = new Packet(GameOpcodes.GS_MAGIC_PROCESS);
        p.WriteByte(MagicSub.DurationExpired);
        p.WriteByte(buffType);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheLongFormIsAMagicReplyForThatSkill()
    {
        var (dispatcher, session) = Readers();
        MagicReply? magic = null;
        int? expired = null;
        session.Combat.MagicEvent += r => magic = r;
        session.Combat.BuffExpiredEvent += t => expired = t;

        dispatcher.Dispatch(LongForm());

        Assert.Null(expired);
        Assert.NotNull(magic);
        var reply = magic.Value;
        Assert.Equal(MagicSub.DurationExpired, reply.Sub);
        Assert.Equal(TransformSkill, reply.SkillId);
        Assert.Equal(CharId, reply.CasterId);
        Assert.Equal(CharId, reply.TargetId);
    }

    [Fact]
    public void TheShortFormIsABuffTypeExpiry()
    {
        var (dispatcher, session) = Readers();
        MagicReply? magic = null;
        int? expired = null;
        session.Combat.MagicEvent += r => magic = r;
        session.Combat.BuffExpiredEvent += t => expired = t;

        dispatcher.Dispatch(ShortForm(StunBuffType));

        Assert.Null(magic);
        Assert.Equal(StunBuffType, expired);
    }
}
