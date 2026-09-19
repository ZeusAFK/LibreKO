using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class MagicSubOpcodeTests
{
    private const int SkillId = 450018;

    [Fact]
    public void CancellingATransformationCarriesNoBody()
    {
        var packet = MagicProcessPacketWriter.CreateCancelTransformation();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_MAGIC_PROCESS);
        packet.ReadByte().Should().Be((byte)MagicProcessOpcode.CancelTransformation);
        packet.RemainingBytes.Should().Be(0, "the client reads nothing from this one");
    }

    [Fact]
    public void ExtendingADurationNamesTheSkill()
    {
        var packet = MagicProcessPacketWriter.CreateExtendDuration(SkillId);

        packet.ReadByte().Should().Be((byte)MagicProcessOpcode.ExtendDuration);
        packet.ReadInt().Should().Be(SkillId);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void TheTransformationListNamesTheSkill()
    {
        var packet = MagicProcessPacketWriter.CreateTransformationList(SkillId);

        packet.ReadByte().Should().Be((byte)MagicProcessOpcode.TransformationList);
        packet.ReadInt().Should().Be(SkillId);
        packet.RemainingBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(TransformationFailure.OutsideCastleSiege, 1)]
    [InlineData(TransformationFailure.BeforeCastleSiegeStarts, 2)]
    [InlineData(TransformationFailure.TooFarFromBase, 3)]
    public void ARefusedTransformationSendsOneReasonByte(TransformationFailure reason, byte expected)
    {
        var packet = MagicProcessPacketWriter.CreateTransformationFailed(reason);

        packet.ReadByte().Should().Be((byte)MagicProcessOpcode.TransformationFailed);
        packet.ReadByte().Should().Be(expected);
        packet.RemainingBytes.Should().Be(0);
    }
}
