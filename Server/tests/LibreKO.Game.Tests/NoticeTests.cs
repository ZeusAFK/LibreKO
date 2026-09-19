using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Tests;

public class NoticeTests
{
    [Fact]
    public void AScreenNoticeIsACountedListOfShortLines()
    {
        var packet = NoticePacketWriter.Screen("Server restarting", "In five minutes");

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_NOTICE);
        packet.ReadByte().Should().Be(NoticePacketWriter.ScreenNotice);
        packet.ReadByte().Should().Be(2);
        packet.ReadSByteString().Should().Be("Server restarting");
        packet.ReadSByteString().Should().Be("In five minutes");
    }

    [Fact]
    public void ALoginNoticeIsACountedListOfTitledEntries()
    {
        var packet = NoticePacketWriter.Login([("Exp Event", "Experience is doubled")]);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_NOTICE);
        packet.ReadByte().Should().Be(NoticePacketWriter.LoginNotice);
        packet.ReadByte().Should().Be(1);
        packet.ReadString().Should().Be("Exp Event");
        packet.ReadString().Should().Be("Experience is doubled");
    }

    [Fact]
    public void ALineTooLongForItsLengthByteIsCut()
    {
        var packet = NoticePacketWriter.Screen(new string('x', 400));

        packet.ReadByte();
        packet.ReadByte();
        packet.ReadSByteString().Length.Should().Be(255);
    }
}
