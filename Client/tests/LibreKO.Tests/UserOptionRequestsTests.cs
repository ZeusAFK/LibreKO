using LibreKO.Network;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class UserOptionRequestsTests
{
    private sealed class RecordingSink : IPacketSink
    {
        public readonly List<byte[]> Sent = [];

        public bool Connected => true;

        public void Send(Packet packet) => Sent.Add(packet.GetBytes());
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void TheTradeBlockIsSentAsTheRetailUserOption(bool blocked, byte flag)
    {
        var sink = new RecordingSink();
        var self = new SelfSession();

        new UserOptionRequests(sink, new UserOptionSession(self), self).TradeState(blocked);

        Assert.Equal(new byte[] { (byte)GameOpcodes.GS_USER_OPTION, (byte)UserOptionSubOpcode.TradeState, flag }, sink.Sent.Single());
    }
}
