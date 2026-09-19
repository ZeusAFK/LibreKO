using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class ProgressionPacketWriterTests
{
    [Fact]
    public void LoyaltyChange_EmitsFourFullWidthTotals()
    {
        var packet = LoyaltyChangePacketWriter.Totals(12_345, 6_789);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_LOYALTY_CHANGE);
        packet.ReadByte().Should().Be((byte)LoyaltySubOpcode.Totals);
        packet.ReadInt().Should().Be(12_345);
        packet.ReadInt().Should().Be(6_789);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void LoyaltyChangeBodyIsSeventeenBytesNotFourteen()
    {
        var packet = LoyaltyChangePacketWriter.Totals(1, 2);

        packet.GetData().Length.Should().Be(1 + 4 + 4 + 4 + 4);
    }

    [Fact]
    public void SkillData_LeadsWithACountAndCarriesNoSubCommand()
    {
        var data = new byte[3 * SkillDataPacketWriter.BytesPerSlot];
        BitConverter.GetBytes(101_001).CopyTo(data, 0);
        BitConverter.GetBytes(201_002).CopyTo(data, 4);
        BitConverter.GetBytes(301_003).CopyTo(data, 8);

        var packet = SkillDataPacketWriter.Slots(data);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_SKILLDATA);
        packet.ReadShort().Should().Be(3);
        packet.ReadInt().Should().Be(101_001);
        packet.ReadInt().Should().Be(201_002);
        packet.ReadInt().Should().Be(301_003);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void LevelChangeCarriesBothExperienceTotals()
    {
        var packet = ProgressionPacketWriter.LevelChange(new ProgressionPacketWriter.LevelState(
            CharacterId: 70_001, Level: 62, StatPoints: 12, MasteryPoints: 3,
            MaxExperience: 9_000_000_000L, Experience: 8_500_000_000L,
            MaxHp: 1800, Hp: 1750, MaxMp: 900, Mp: 880,
            MaxWeight: 3000, ItemWeight: 1200));
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_LEVEL_CHANGE);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadByte().Should().Be(62);
        packet.ReadShort().Should().Be(12);
        packet.ReadByte().Should().Be(3);
        packet.ReadLong().Should().Be(9_000_000_000L);
        packet.ReadLong().Should().Be(8_500_000_000L);
        packet.ReadShort().Should().Be(1800);
        packet.ReadShort().Should().Be(1750);
        packet.ReadShort().Should().Be(900);
        packet.ReadShort().Should().Be(880);
        packet.ReadInt().Should().Be(3000);
        packet.ReadInt().Should().Be(1200);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void WeightChangeIsASingleInt()
    {
        var packet = ProgressionPacketWriter.WeightChange(1200);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_WEIGHT_CHANGE);
        packet.ReadInt().Should().Be(1200);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void SkillDataClearIsAZeroCount()
    {
        var packet = SkillDataPacketWriter.Cleared();
        packet.ResetOffset();

        packet.ReadShort().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
