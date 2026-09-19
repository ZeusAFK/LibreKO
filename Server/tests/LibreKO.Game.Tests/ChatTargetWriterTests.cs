using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class ChatTargetWriterTests
{
    [Fact]
    public void Connected_EmitsRetailShape()
    {
        var packet = ChatTargetPacketWriter.WhisperConnected("Aurelia");
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CHAT_TARGET);
        packet.ReadByte().Should().Be(ChatTargetPacketWriter.TypeWhisper);
        packet.ReadShort().Should().Be((short)ChatTargetResult.Connected);
        packet.ReadShort().Should().Be(7);
        packet.ReadBytes(7).Should().Equal("Aurelia"u8.ToArray());
        packet.ReadByte().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void NameLengthPrefixIsTwoBytes()
    {
        var packet = ChatTargetPacketWriter.WhisperConnected("Aurelia");
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadShort();

        packet.ReadShort().Should().Be(7);
    }

    [Fact]
    public void TargetNotFound_SendsEmptyNameAndKeepsShape()
    {
        var packet = ChatTargetPacketWriter.WhisperTargetNotFound();
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ChatTargetPacketWriter.TypeWhisper);
        packet.ReadShort().Should().Be((short)ChatTargetResult.TargetNotFound);
        packet.ReadShort().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(-3)]
    public void RejectionsCarryTheTargetName(short expected)
    {
        var writer = expected switch
        {
            (short)ChatTargetResult.TargetBlocked => ChatTargetPacketWriter.WhisperTargetBlocked("Dorin"),
            (short)ChatTargetResult.CrossNationZone => ChatTargetPacketWriter.WhisperCrossNationZone("Dorin"),
            _ => ChatTargetPacketWriter.WhisperSenderBlocked("Dorin"),
        };

        var packet = writer;
        packet.ResetOffset();

        packet.ReadByte().Should().Be(ChatTargetPacketWriter.TypeWhisper);
        packet.ReadShort().Should().Be(expected);
        packet.ReadShort().Should().Be(5);
    }

    [Fact]
    public void ClanAdmissionCarriesDirectionInTrailingByte()
    {
        var received = ChatTargetPacketWriter.ClanAdmissionRequestReceived("Dorin");
        received.ResetOffset();
        received.ReadByte().Should().Be(ChatTargetPacketWriter.TypeClanAdmission);
        received.ReadShort();
        received.ReadShort();
        received.ReadBytes(5);
        received.ReadByte().Should().Be(ChatTargetPacketWriter.AdmissionRequestReceived);

        var sent = ChatTargetPacketWriter.ClanAdmissionRequestSent("Dorin");
        sent.ResetOffset();
        sent.ReadByte();
        sent.ReadShort();
        sent.ReadShort();
        sent.ReadBytes(5);
        sent.ReadByte().Should().Be(ChatTargetPacketWriter.AdmissionRequestSent);
    }
}
