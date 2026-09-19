using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class ChatPacketWriterTests
{
    [Fact]
    public void Say_EmitsRetailShapeWithASingleTrailingAuthorityByte()
    {
        var packet = ChatPacketWriter
            .Say(ChatPacketWriter.TypeGeneral, 1, 4321, "Aurelia", "hello", isGameMaster: false)
            ;
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CHAT);
        packet.ReadByte().Should().Be(ChatPacketWriter.TypeGeneral);
        packet.ReadByte().Should().Be(1);
        packet.ReadInt().Should().Be(4321);
        packet.ReadSByteString().Should().Be("Aurelia");
        packet.ReadString().Should().Be("hello");
        packet.ReadByte().Should().Be(ChatPacketWriter.AuthorityPlayer);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void NameUsesAByteLengthAndMessageUsesAShortLength()
    {
        var packet = ChatPacketWriter
            .Say(ChatPacketWriter.TypeGeneral, 1, 1, "Ab", "cde", isGameMaster: false)
            ;
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt();

        packet.ReadByte().Should().Be(2);
        packet.ReadBytes(2);
        packet.ReadShort().Should().Be(3);
    }

    [Fact]
    public void GameMasterAuthorityIsFlagged()
    {
        var packet = ChatPacketWriter
            .Say(ChatPacketWriter.TypeGameMaster, 2, 7, "Gm", "hi", isGameMaster: true);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadSByteString();
        packet.ReadString();

        packet.ReadByte().Should().Be(ChatPacketWriter.AuthorityGameMaster);
    }

    [Fact]
    public void SystemNoticeHasNoSenderAndNoTrailingByte()
    {
        var packet = ChatPacketWriter.SystemNotice(1, "server restarting");
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ChatPacketWriter.TypeSystemNotice);
        packet.ReadByte().Should().Be(1);
        packet.ReadInt().Should().Be(ChatPacketWriter.NoSender);
        packet.ReadSByteString().Should().BeEmpty();
        packet.ReadString().Should().Be("server restarting");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void NationNoticeKeepsItsSenderSoTheAuthorityByteIsSent()
    {
        var packet = ChatPacketWriter.NationNotice(2, 99, "King", "tax raised");
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt().Should().Be(99);
        packet.ReadSByteString().Should().Be("King");
        packet.ReadString().Should().Be("tax raised");

        packet.ReadByte().Should().Be(ChatPacketWriter.AuthorityNone);
        packet.RemainingBytes.Should().Be(0);
    }
}
