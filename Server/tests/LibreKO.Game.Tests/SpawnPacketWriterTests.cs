using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class SpawnPacketWriterTests
{
    private static NpcSpawnPacketWriter.NpcState Monster => new(
        UniqueId: 7001, NpcId: 240, IsMonster: true, ModelId: 1240, SellingGroup: 0,
        NpcType: 12, Size: 100, WeaponRight: 0, WeaponLeft: 0, Nation: 1, Level: 62,
        X: 5430, Z: 3770, Y: 120, GateOpen: false, ObjectType: 0, Direction: 90);

    [Fact]
    public void NpcSpawn_MatchesTheFifteenFieldsRetailReads()
    {
        var packet = NpcSpawnPacketWriter.In(1, Monster);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_NPC_INOUT);
        packet.ReadByte().Should().Be(1);
        packet.ReadInt().Should().Be(7001);
        packet.ReadShort().Should().Be(240);
        packet.ReadByte().Should().Be((byte)NpcSpawnKind.Monster);
        packet.ReadShort().Should().Be(1240);
        packet.ReadInt().Should().Be(0);
        packet.ReadByte().Should().Be(12);
        packet.ReadInt().Should().Be(0);
        packet.ReadShort().Should().Be(100);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.ReadByte().Should().Be(62);
        packet.ReadShort().Should().Be(5430);
        packet.ReadShort().Should().Be(3770);
        packet.ReadShort().Should().Be(120);
    }

    [Fact]
    public void MonstersAlwaysReportNationZero()
    {
        var packet = NpcSpawnPacketWriter.In(1, Monster with { Nation = 2 });
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadInt();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadShort();
        packet.ReadInt();
        packet.ReadInt();

        packet.ReadByte().Should().Be(0);
    }

    [Fact]
    public void NonMonstersKeepTheirNationAndReportKindNpc()
    {
        var packet = NpcSpawnPacketWriter.In(1, Monster with { IsMonster = false, Nation = 2 });
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadShort();
        packet.ReadByte().Should().Be((byte)NpcSpawnKind.Npc);
        packet.ReadShort();
        packet.ReadInt();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadShort();
        packet.ReadInt();
        packet.ReadInt();

        packet.ReadByte().Should().Be(2);
    }

    [Fact]
    public void DespawnCarriesOnlyTheUniqueId()
    {
        var packet = NpcSpawnPacketWriter.Out(2, 7001);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(2);
        packet.ReadInt().Should().Be(7001);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Move_MatchesRetailThirteenByteShape()
    {
        var packet = MovementPacketWriter.Move(70_001, 5430, 3770, 120, 45, 1);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_MOVE);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadUShort().Should().Be(5430);
        packet.ReadUShort().Should().Be(3770);
        packet.ReadUShort().Should().Be(120);
        packet.ReadShort().Should().Be(45);
        packet.ReadByte().Should().Be(1);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Attack_AlwaysCarriesTheOptionalCriticalByte()
    {
        var packet = AttackPacketWriter.Create(
            AttackPacketWriter.TypeMelee, AttackResult.Succeeded, 70_001, 70_002);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_ATTACK);
        packet.ReadByte().Should().Be(AttackPacketWriter.TypeMelee);
        packet.ReadByte().Should().Be((byte)AttackResult.Succeeded);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadInt().Should().Be(70_002);
        packet.ReadByte().Should().Be(AttackPacketWriter.NoCritical);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Rotate_IsSixBytes()
    {
        var packet = MovementPacketWriter.Rotate(70_001, 180);
        packet.ResetOffset();

        packet.ReadInt().Should().Be(70_001);
        packet.ReadShort().Should().Be(180);
        packet.RemainingBytes.Should().Be(0);
    }
}
