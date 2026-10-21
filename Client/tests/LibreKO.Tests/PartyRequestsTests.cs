using LibreKO.Network;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class PartyRequestsTests
{
    private const int SelfId = 7;
    private const int LeaderId = 9;
    private const string LeaderName = "Velkan";
    private const byte Joined = 1;

    private sealed class NoConnection : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    private sealed class Recorder : IPacketSink
    {
        public readonly List<Packet> Sent = new();

        public bool Connected => true;

        public void Send(Packet packet) => Sent.Add(packet);
    }

    [Fact]
    public void WithdrawingAnInviteNamesTheInvitee()
    {
        var sink = new Recorder();
        var requests = new PartyRequests(sink, new PartySession(new SelfSession()), new PartyWarBoardSession());

        requests.CancelInvite(LeaderId);

        var packet = Assert.Single(sink.Sent);
        packet.ResetOffset();
        Assert.Equal((byte)GameOpcodes.GS_PARTY, packet.GetOpcode());
        Assert.Equal(PartyRequest.InviteCancel, packet.ReadByte());
        Assert.Equal(LeaderId, packet.ReadInt());
        Assert.Equal(0, packet.RemainingBytes);
    }

    [Fact]
    public void AcceptingABoardInviteWaitsForTheServerToListUs()
    {
        var self = new SelfSession();
        self.ApplyCharacterId(SelfId);
        var party = new PartySession(self);
        var requests = new PartyRequests(new NoConnection(), party, new PartyWarBoardSession());

        requests.BbsAccept(LeaderName);
        party.Apply(new PartyMember(LeaderId, Joined, LeaderName, 100, 90, 60, 105, 50, 40));
        party.Apply(new PartyMember(SelfId, Joined, "Rikka", 100, 90, 60, 105, 50, 40));

        Assert.True(party.IsLeader(LeaderId));
        Assert.False(party.IsLeader(SelfId));
        Assert.False(party.Joining);
    }
}
