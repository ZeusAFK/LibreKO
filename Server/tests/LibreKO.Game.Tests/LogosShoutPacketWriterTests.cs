using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class LogosShoutPacketWriterTests
{
    [Fact]
    public void RegisterRejectedCarriesRetailsNegativeResultCode()
    {
        var packet = LogosShoutPacketWriter.RegisterRejected(LogosShoutPacketWriter.RegisterNoItem);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_LOGOSSHOUT);
        packet.ReadByte().Should().Be((byte)LogosShoutSubOpcode.RegisterResult);
        ((sbyte)packet.ReadByte()).Should().Be(LogosShoutPacketWriter.RegisterNoItem);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void RegisterAcceptedAppendsTheMessageNumber()
    {
        var packet = LogosShoutPacketWriter.RegisterAcceptedAs(7);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)LogosShoutSubOpcode.RegisterResult);
        ((sbyte)packet.ReadByte()).Should().Be(LogosShoutPacketWriter.RegisterAccepted);
        packet.ReadByte().Should().Be(7);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void UpgradeAnnouncementEndsWithTheNationByteRetailColoursTheNameWith()
    {
        var packet = LogosShoutPacketWriter.UpgradeAnnouncement(1, "Aurelia", 123456, 2);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)LogosShoutSubOpcode.Broadcast);
        packet.ReadByte().Should().Be(LogosShoutPacketWriter.AnnouncementUpgrade);
        packet.ReadByte().Should().Be(1);
        packet.ReadSByteString().Should().Be("Aurelia");
        packet.ReadInt().Should().Be(123456);
        packet.ReadByte().Should().Be(0);
        packet.ReadByte().Should().Be(2);
        packet.RemainingBytes.Should().Be(0);
    }
}
