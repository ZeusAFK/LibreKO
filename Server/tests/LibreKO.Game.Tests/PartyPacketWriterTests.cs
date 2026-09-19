using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class PartyPacketWriterTests
{
    private static PartyPacketWriter.MemberState Member => new(
        CharacterId: 70_001, Name: "Aurelia", Level: 62, Class: 105,
        MaxHp: 1800, Hp: 1750, MaxMp: 900, Mp: 880);

    [Fact]
    public void MemberInfo_MatchesTheRetailRecord()
    {
        var packet = PartyPacketWriter.MemberInfo(Member);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_PARTY);
        packet.ReadByte().Should().Be((byte)PartySubOpcode.MemberInfo);
        packet.ReadShort().Should().Be(PartyPacketWriter.StatusMemberRecord);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadByte().Should().Be(PartyPacketWriter.MemberJoined);
        packet.ReadString().Should().Be("Aurelia");
        packet.ReadShort().Should().Be(1800);
        packet.ReadShort().Should().Be(1750);
        packet.ReadByte().Should().Be(62);
        packet.ReadShort().Should().Be(105);
        packet.ReadShort().Should().Be(900);
        packet.ReadShort().Should().Be(880);
        packet.ReadByte().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(PartyPacketWriter.NoTargetMark);
        packet.ReadByte().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void MemberInfoLeadsWithAStatusNotACharacterId()
    {
        var packet = PartyPacketWriter.MemberInfo(Member);
        packet.ResetOffset();
        packet.ReadByte();

        packet.ReadShort().Should().Be(1);
    }

    [Fact]
    public void RejectionSharesSubTwoAndCarriesANegativeStatus()
    {
        var packet = PartyPacketWriter.Rejected(-3);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.MemberInfo);
        packet.ReadShort().Should().Be(-3);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PromotionReusesMemberInfoWithTheLeaderStatusCode()
    {
        var packet = PartyPacketWriter
            .MemberInfo(Member, PartyPacketWriter.MemberPromotedToLeader);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadInt();

        packet.ReadByte().Should().Be(PartyPacketWriter.MemberPromotedToLeader);
    }

    [Fact]
    public void InviteIsSubTwoWithAFullWidthCharacterId()
    {
        var packet = PartyPacketWriter.Invite(70_001, "Aurelia");
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.Invite);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadString().Should().Be("Aurelia");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void MemberLeftIsSubFour()
    {
        var packet = PartyPacketWriter.MemberLeft(70_001);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.MemberLeft);
        packet.ReadInt().Should().Be(70_001);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void DisbandIsSubFiveAndCarriesNoPayload()
    {
        var packet = PartyPacketWriter.Disband();
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.Disband);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void VitalsIsSubSixWithFourShorts()
    {
        var packet = PartyPacketWriter.VitalsChange(70_001, 1800, 1750, 900, 880);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.VitalsChange);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadShort().Should().Be(1800);
        packet.ReadShort().Should().Be(1750);
        packet.ReadShort().Should().Be(900);
        packet.ReadShort().Should().Be(880);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void LevelChangeCarriesOnlyTheLevel()
    {
        var packet = PartyPacketWriter.LevelChange(70_001, 62);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.LevelChange);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadByte().Should().Be(62);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ClassChangeIsSubEight()
    {
        var packet = PartyPacketWriter.ClassChange(70_001, 105);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.ClassChange);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadShort().Should().Be(105);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void StatusEffectIsSubNine()
    {
        var packet = PartyPacketWriter.StatusEffect(70_001, 4, applied: true);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.StatusEffect);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadByte().Should().Be(4);
        packet.ReadByte().Should().Be(1);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void CommanderIsSub30WithATwoByteNamePrefix()
    {
        var packet = PartyPacketWriter.Commander(70_001, "Aurelia");
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.Commander);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadShort().Should().Be(7);
        packet.ReadBytes(7).Should().Equal("Aurelia"u8.ToArray());
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void TargetNumberIsSub31AndPreservesNegativeCodes()
    {
        var packet = PartyPacketWriter.TargetNumber(70_001, -1);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.TargetNumber);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadByte().Should().Be(0xFF);
    }

    [Fact]
    public void AlertIsSub32()
    {
        var packet = PartyPacketWriter.Alert(3);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PartySubOpcode.Alert);
        packet.ReadByte().Should().Be(3);
        packet.RemainingBytes.Should().Be(0);
    }
}
