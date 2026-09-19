using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class NameChangePacketWriterTests
{
    private const byte ShowDialog = 1;
    private const byte Renamed = 3;
    private const byte ClanRenamedCode = 16;

    [Fact]
    public void ACharacterReplyCarriesTheCodeAndNothingElse()
    {
        var packet = NameChangeReply(MiscPacketWriter.NameChangeResult(ShowDialog));

        packet.ReadByte().Should().Be(ShowDialog);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ARenameSucceedsWithNoNameInTheBody()
    {
        var packet = NameChangeReply(MiscPacketWriter.NameChangeResult(Renamed));

        packet.ReadByte().Should().Be(Renamed);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AClanReplyPutsTheMarkerBeforeTheCode()
    {
        var packet = NameChangeReply(MiscPacketWriter.ClanNameChangeResult(ShowDialog));

        packet.ReadByte().Should().Be(MiscPacketWriter.ClanNameChangeMarker);
        packet.ReadByte().Should().Be(ShowDialog);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AClanRenameCarriesTheNewNameAfterTheCode()
    {
        var packet = NameChangeReply(MiscPacketWriter.ClanRenamed(ClanRenamedCode, "Aurelians"));

        packet.ReadByte().Should().Be(MiscPacketWriter.ClanNameChangeMarker);
        packet.ReadByte().Should().Be(ClanRenamedCode);
        packet.ReadString().Should().Be("Aurelians");
        packet.RemainingBytes.Should().Be(0);
    }

    private static Packet NameChangeReply(Packet packet)
    {
        packet.ResetOffset();
        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_NAME_CHANGE);
        return packet;
    }
}
