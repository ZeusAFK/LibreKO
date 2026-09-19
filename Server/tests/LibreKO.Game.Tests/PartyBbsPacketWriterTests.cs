using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class PartyBbsPacketWriterTests
{
    private const byte SubList = 0x0B;
    private const byte SubRegister = 0x01;

    [Fact]
    public void ResultLeadsWithTheModeByteRetailReadsFirst()
    {
        var packet = PartyBbsPacketWriter.Result(
            PartyBbsPacketWriter.ModeNormal, SubRegister, PartyBbsPacketWriter.Succeeded);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_PARTY_BBS);
        packet.ReadByte().Should().Be(PartyBbsPacketWriter.ModeNormal);
        packet.ReadByte().Should().Be(SubRegister);
        packet.ReadByte().Should().Be(PartyBbsPacketWriter.Succeeded);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PageHeaderIsThreeShortsWithTheCountInTheMiddle()
    {
        var packet = PartyBbsPacketWriter.Page(
            PartyBbsPacketWriter.ModeNormal, SubList, 2, 7,
            [
                new PartyBbsPacketWriter.BoardEntry(
                    "Aurelia", 105, 62, PartyBbsPacketWriter.EntrySeeker, "lfg", 21, 0, 1),
                new PartyBbsPacketWriter.BoardEntry(
                    "Dorin", 102, 60, PartyBbsPacketWriter.EntryWantedParty, "need healer", 24, 5, 2),
            ]);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(PartyBbsPacketWriter.ModeNormal);
        packet.ReadByte().Should().Be(SubList);
        packet.ReadByte().Should().Be(PartyBbsPacketWriter.Succeeded);
        packet.ReadShort().Should().Be(2);
        packet.ReadShort().Should().Be(2);
        packet.ReadShort().Should().Be(7);
    }

    [Fact]
    public void PageEntryIsFifteenFixedBytesAroundTwoStrings()
    {
        var packet = PartyBbsPacketWriter.Page(
            PartyBbsPacketWriter.ModeNormal, SubList, 0, 1,
            [
                new PartyBbsPacketWriter.BoardEntry(
                    "Aurelia", 105, 62, PartyBbsPacketWriter.EntrySeeker, "lfg", 21, 3, 1),
            ]);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadShort();

        packet.ReadString().Should().Be("Aurelia");
        packet.ReadInt().Should().Be(105);
        packet.ReadByte().Should().Be(62);
        packet.ReadByte().Should().Be(PartyBbsPacketWriter.EntrySeeker);
        packet.ReadByte().Should().Be(0);
        packet.ReadSByteString().Should().Be("lfg");
        packet.ReadShort().Should().Be(21);
        packet.ReadByte().Should().Be(3);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(0);

        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PageSendsExactlyTheDeclaredCountWithNoTrailingPadding()
    {
        var packet = PartyBbsPacketWriter.Page(
            PartyBbsPacketWriter.ModeNormal, SubList, 0, 1, []);
        packet.ResetOffset();

        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadShort().Should().Be(0);
        packet.ReadShort();

        packet.RemainingBytes.Should().Be(0);
    }
}
