using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class BountyReaderTests
{
    private const int BountyId = 42;

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

    private static Packet Result(BountySubOpcode sub, BountyResult result)
    {
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte((byte)sub);
        p.WriteByte((byte)result);
        p.WriteInt(BountyId);
        p.ResetOffset();
        return p;
    }

    [Theory]
    [InlineData(BountyResult.Succeeded, true)]
    [InlineData(BountyResult.Failed, false)]
    public void AWithdrawResultReachesTheBoard(BountyResult result, bool ok)
    {
        var (dispatcher, session) = Readers();
        BountyOutcome? announced = null;
        session.Bounty.BountyWithdrawEvent += outcome => announced = outcome;

        dispatcher.Dispatch(Result(BountySubOpcode.Withdraw, result));

        Assert.Equal(new BountyOutcome(BountyId, ok), announced);
    }

    [Theory]
    [InlineData(BountyStanding.Open)]
    [InlineData(BountyStanding.Own)]
    [InlineData(BountyStanding.Earned)]
    public void AListRowKeepsItsStanding(BountyStanding standing)
    {
        var (dispatcher, session) = Readers();
        List<BountyEntry>? listed = null;
        session.Bounty.BountyListEvent += list => listed = list;
        var p = new Packet(GameOpcodes.GS_BOUNTY);
        p.WriteByte((byte)BountySubOpcode.List);
        p.WriteUShort(1);
        p.WriteInt(BountyId);
        p.WriteSByteString("Target");
        p.WriteSByteString("Poster");
        p.WriteInt(BountyId);
        p.WriteByte((byte)standing);
        p.ResetOffset();

        dispatcher.Dispatch(p);

        Assert.Equal(standing, Assert.Single(listed!).Standing);
    }

    [Fact]
    public void AClaimResultReachesTheBoard()
    {
        var (dispatcher, session) = Readers();
        BountyOutcome? announced = null;
        session.Bounty.BountyClaimEvent += outcome => announced = outcome;

        dispatcher.Dispatch(Result(BountySubOpcode.Claim, BountyResult.Succeeded));

        Assert.Equal(new BountyOutcome(BountyId, true), announced);
    }

    [Fact]
    public void AWithdrawResultIsNotTakenForAClaim()
    {
        var (dispatcher, session) = Readers();
        var claims = 0;
        session.Bounty.BountyClaimEvent += _ => claims++;

        dispatcher.Dispatch(Result(BountySubOpcode.Withdraw, BountyResult.Failed));

        Assert.Equal(0, claims);
    }
}
