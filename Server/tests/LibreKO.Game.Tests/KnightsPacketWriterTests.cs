using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class KnightsPacketWriterTests
{
    [Fact]
    public void ResultIsSubThenCode()
    {
        var packet = KnightsPacketWriter.Result(KnightsSubOpcode.Withdraw, KnightsPacketWriter.Failed);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Withdraw);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Failed);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClanCreatedCarriesTheFullSuccessRecord()
    {
        var packet = KnightsPacketWriter.ClanCreated(4321, 42, "Wolves", 3, 1_000_000);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Create);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        packet.ReadInt().Should().Be(4321);
        packet.ReadShort().Should().Be(42);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadByte().Should().Be(3);
        packet.ReadByte().Should().Be(3);
        packet.ReadInt().Should().Be(1_000_000);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void JoinAcceptedCarriesTheClanLookAndCape()
    {
        var packet = KnightsPacketWriter.JoinAccepted(new KnightsPacketWriter.JoinedState(
            CharacterId: 4321, ClanId: 42, ClanName: "Wolves", Fame: 5, ClanType: 3,
            MarkVersion: 9, CapeId: 133, CapeColour: 0x030201, Grade: 1));
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.Join);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        packet.ReadInt().Should().Be(4321);
        packet.ReadShort().Should().Be(42);
        packet.ReadByte().Should().Be(5);
        packet.ReadByte().Should().Be(3);
        packet.ReadShort().Should().Be(KnightsPacketWriter.Reserved);
        packet.ReadShort().Should().Be(133);
        packet.ReadInt().Should().Be(0x030201);
        packet.ReadShort().Should().Be(9);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(1);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClanBrowseListLeadsWithItsPageByte()
    {
        var packet = KnightsPacketWriter.ClanBrowseList(new[]
        {
            new KnightsPacketWriter.BrowseEntry(42, "Wolves", "Aurelia", 12, 3, 361_400),
        });
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_KNIGHTS_LIST);
        packet.ReadByte().Should().Be(KnightsPacketWriter.BrowseListPage);
        packet.ReadShort().Should().Be(1);
        packet.ReadShort().Should().Be(42);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadString().Should().Be("Aurelia");
        packet.ReadShort().Should().Be(12);
        packet.ReadByte().Should().Be(3);
        packet.ReadInt().Should().Be(361_400);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void MemberListCarriesTheRetailPageHeaderAndEightFieldEntries()
    {
        var packet = KnightsPacketWriter.MemberList(
            KnightsSubOpcode.MemberRequest, 42, "Wolves", 7, new[]
            {
                new KnightsPacketWriter.Member("Aurelia", 1, 62, 105, true),
                new KnightsPacketWriter.Member("Dorin", 5, 60, 102, false),
            });
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.MemberRequest);
        packet.ReadByte().Should().Be(KnightsPacketWriter.MemberListPage);
        packet.ReadShort().Should().Be(0);
        packet.ReadShort().Should().Be(7);
        packet.ReadShort().Should().Be(42);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadShort().Should().Be(2);

        packet.ReadString().Should().Be("Aurelia");
        packet.ReadByte().Should().Be(1);
        packet.ReadSByteString().Should().Be(string.Empty);
        packet.ReadByte().Should().Be(62);
        packet.ReadShort().Should().Be(105);
        packet.ReadByte().Should().Be(1);
        packet.ReadString().Should().Be(string.Empty);
        packet.ReadInt().Should().Be(0);

        packet.ReadString().Should().Be("Dorin");
        packet.ReadByte().Should().Be(5);
        packet.ReadSByteString().Should().Be(string.Empty);
        packet.ReadByte().Should().Be(60);
        packet.ReadShort().Should().Be(102);
        packet.ReadByte().Should().Be(0);
        packet.ReadString().Should().Be(string.Empty);
        packet.ReadInt().Should().Be(0);

        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void EmptyMemberListIsAZeroCountNotABareFailure()
    {
        var packet = KnightsPacketWriter.EmptyMemberList(KnightsSubOpcode.MemberRequest, 0, string.Empty);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.MemberRequest);
        packet.ReadByte().Should().Be(KnightsPacketWriter.MemberListPage);
        packet.ReadShort().Should().Be(0);
        packet.ReadShort().Should().Be(0);
        packet.ReadShort().Should().Be(0);
        packet.ReadString().Should().Be(string.Empty);
        packet.ReadShort().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClanInfoCarriesIdNameFlagMembersAndChief()
    {
        var packet = KnightsPacketWriter.ClanInfo(
            KnightsSubOpcode.CurrentRequest, 42, "Wolves", 3, 12, "Aurelia",
            grade: 2, points: 361_400, pointFund: 5_000, notice: "raid at eight");
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.CurrentRequest);
        packet.ReadByte().Should().Be(KnightsPacketWriter.Succeeded);
        packet.ReadShort().Should().Be(42);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadByte().Should().Be(3);
        packet.ReadShort().Should().Be(12);
        packet.ReadString().Should().Be("Aurelia");
        packet.ReadByte().Should().Be(2);
        packet.ReadInt().Should().Be(361_400);
        packet.ReadInt().Should().Be(5_000);
        packet.ReadString().Should().Be("raid at eight");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClanMarkPrefixesTheBlobWithItsLength()
    {
        var mark = new byte[] { 1, 2, 3, 4, 5 };
        var packet = KnightsPacketWriter.ClanMark(KnightsSubOpcode.MarkReq, 1, 2, 42, 7, mark);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.MarkReq);
        packet.ReadUShort().Should().Be(1);
        packet.ReadUShort().Should().Be(2);
        packet.ReadUShort().Should().Be(42);
        packet.ReadUShort().Should().Be(7);
        packet.ReadUShort().Should().Be((ushort)mark.Length);
        packet.ReadBytes(mark.Length).Should().Equal(mark);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void CapeUsesItsOwnOpcodeNotTheKnightsOne()
    {
        var packet = KnightsPacketWriter.Cape(42, 12, 1, 2, 3);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CAPE);
        packet.ReadShort().Should().Be((short)CapeResult.Changed);
        packet.ReadShort().Should().Be(42);
        packet.ReadShort().Should().Be(KnightsPacketWriter.Reserved);
        packet.ReadShort().Should().Be(12);
        packet.ReadInt().Should().Be(0x030201);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void CapeRefusalIsASignedShort()
    {
        var packet = KnightsPacketWriter.CapeRefused(CapeResult.MissingPurchaseItem);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CAPE);
        packet.ReadShort().Should().Be(-8);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void NoticeRefusalTravelsOnItsOwnSub()
    {
        var packet = KnightsPacketWriter.NoticeRefused(KnightsNoticeResult.NoAuthority);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)KnightsSubOpcode.NoticeResult);
        packet.ReadByte().Should().Be((byte)KnightsNoticeResult.NoAuthority);
        packet.RemainingBytes.Should().Be(0);
    }
}
