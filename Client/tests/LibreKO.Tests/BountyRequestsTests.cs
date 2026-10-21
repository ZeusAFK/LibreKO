using LibreKO.Network;
using LibreKO.Network.Requests;
using Xunit;

namespace LibreKO.Tests;

public class BountyRequestsTests
{
    private const int BountyId = 42;

    private sealed class RecordingSink : IPacketSink
    {
        public List<Packet> Sent { get; } = [];

        public bool Connected => true;

        public void Send(Packet packet) => Sent.Add(packet);
    }

    [Fact]
    public void AWithdrawNamesTheBountyUnderItsOwnSubOpcode()
    {
        var sink = new RecordingSink();

        new BountyRequests(sink).Withdraw(BountyId);

        var packet = Assert.Single(sink.Sent);
        Assert.Equal((byte)GameOpcodes.GS_BOUNTY, packet.GetOpcode());
        packet.ResetOffset();
        Assert.Equal((byte)BountySubOpcode.Withdraw, packet.ReadByte());
        Assert.Equal(BountyId, packet.ReadInt());
    }
}
