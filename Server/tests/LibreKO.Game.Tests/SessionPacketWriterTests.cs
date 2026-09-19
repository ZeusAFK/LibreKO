using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class SessionPacketWriterTests
{
    [Fact]
    public void VersionCheck_EmitsTheFourFieldsTheClientReads()
    {
        var packet = SessionPacketWriter.VersionCheck(2618);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_VERSION_CHECK);
        packet.ReadByte().Should().Be((byte)GameServerMode.Normal,
            "a one here puts the client in PVP-server mode: a different cloak table, and a "
            + "premium-PVP greeting in Moradon");
        packet.ReadShort().Should().Be(2618);
        packet.ReadByte().Should().Be(SessionPacketWriter.NoCryptionKey,
            "a non-zero length is followed by that many key bytes and turns encryption on");
        packet.ReadByte().Should().Be((byte)GameServerState.Open);
        packet.RemainingBytes.Should().Be(0);
    }
}
