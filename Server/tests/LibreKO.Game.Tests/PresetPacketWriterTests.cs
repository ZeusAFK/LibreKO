using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class PresetPacketWriterTests
{
    [Fact]
    public void StatAppliedMatchesTheRetailStatPresetReply()
    {
        var packet = PresetPacketWriter.StatApplied(new PresetPacketWriter.StatState(
            Strength: 120, Stamina: 90, Dexterity: 60, Intelligence: 50, Magic: 50,
            StatPoints: 14, MaxHp: 3200, MaxMp: 1100, TotalHit: 480, MaxWeight: 12500));
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_PRESET);
        packet.ReadByte().Should().Be((byte)PresetSubOpcode.Stat);
        packet.ReadByte().Should().Be(PresetPacketWriter.Applied);
        packet.ReadByte().Should().Be(120);
        packet.ReadByte().Should().Be(90);
        packet.ReadByte().Should().Be(60);
        packet.ReadByte().Should().Be(50);
        packet.ReadByte().Should().Be(50);
        packet.ReadShort().Should().Be(14);
        packet.ReadShort().Should().Be(3200);
        packet.ReadShort().Should().Be(1100);
        packet.ReadShort().Should().Be(480);
        packet.ReadInt().Should().Be(12500);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void SkillAppliedCarriesTheFourClassMasteriesAndThePool()
    {
        var packet = PresetPacketWriter.SkillApplied(
            new PresetPacketWriter.SkillState(71, 42, 13, 5, 9));
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)PresetSubOpcode.Skill);
        packet.ReadByte().Should().Be(PresetPacketWriter.Applied);
        packet.ReadByte().Should().Be(71);
        packet.ReadByte().Should().Be(42);
        packet.ReadByte().Should().Be(13);
        packet.ReadByte().Should().Be(5);
        packet.ReadByte().Should().Be(9);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void RejectionCarriesOnlyTheSubAndTheResultCode()
    {
        var stat = PresetPacketWriter.StatRejected(PresetPacketWriter.StatNeedsRedistribution);
        stat.ResetOffset();
        stat.ReadByte().Should().Be((byte)PresetSubOpcode.Stat);
        stat.ReadByte().Should().Be(PresetPacketWriter.StatNeedsRedistribution);
        stat.RemainingBytes.Should().Be(0);

        var skill = PresetPacketWriter.SkillRejected(PresetPacketWriter.SkillNeedsSecondJobChange);
        skill.ResetOffset();
        skill.ReadByte().Should().Be((byte)PresetSubOpcode.Skill);
        skill.ReadByte().Should().Be(PresetPacketWriter.SkillNeedsSecondJobChange);
        skill.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PresetSitsOnTheOpcodeRetailDispatches()
    {
        ((byte)GameOpcodes.GS_PRESET).Should().Be(0xB9);
    }
}
