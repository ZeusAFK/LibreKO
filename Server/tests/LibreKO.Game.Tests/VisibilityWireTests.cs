using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class VisibilityWireTests
{
    private const int TailBeforeClanFields = 2 + 1 + 2 + 4 + 1 + 4 + 2 + 4 + 4 + 1 + 1 + 2 + 2 + 2 + 4 + 1;

    private static NpcSpawnPacketWriter.NpcState Npc(short direction) => new(
        UniqueId: 9001, NpcId: 30200, IsMonster: false, ModelId: 30200, SellingGroup: 0,
        NpcType: 11, Size: 100, WeaponRight: 0, WeaponLeft: 0, Nation: 1, Level: 20,
        X: 5430, Z: 3770, Y: 120, GateOpen: false, ObjectType: 0, Direction: direction);

    private static Packet Record(short direction)
    {
        var packet = new Packet(GameOpcodes.GS_NPC_INOUT);
        NpcSpawnPacketWriter.WriteRecord(packet, Npc(direction));
        packet.ResetOffset();
        packet.ReadBytes(TailBeforeClanFields);
        return packet;
    }

    [Fact]
    public void TheNpcRecordEndsWithTheClanFieldsThenTwoSeparateBytes()
    {
        var packet = Record(225);

        packet.ReadShort().Should().Be(NpcSpawnPacketWriter.NoClan);
        packet.ReadShort().Should().Be(NpcSpawnPacketWriter.NoClanMarkVersion);
        packet.ReadByte().Should().Be(225);
        packet.ReadByte().Should().Be(NpcSpawnPacketWriter.NoEventRoom);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ADirectionWiderThanAByteCannotReachTheFieldAfterIt()
    {
        var packet = Record(300);

        packet.ReadShort();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadByte().Should().Be(
            NpcSpawnPacketWriter.NoEventRoom,
            because: "the last two bytes of the record are separate fields, not one direction");
    }

    [Fact]
    public void TheUserRecordCarriesTheAppearanceStateBeforeTheTransformId()
    {
        var visuals = Enumerable.Range(0, 17)
            .Select(i => new UserInfoPacketWriter.VisualItem(0, 0, 0))
            .ToArray();

        var packet = new Packet(GameOpcodes.GS_USER_INOUT);
        UserInfoPacketWriter.WriteRecord(packet, new UserInfoPacketWriter.UserState(
            Name: "Aurelia", Nation: 1, KnightsId: 0, KnightsFame: 0, Clan: null,
            NoClanNationCode: 93, Level: 62, Race: 1, Class: 105,
            X: 5430, Z: 3770, Y: 120, Face: 2, Hair: 7,
            Pose: (byte)UserPoseState.Standing,
            NeedParty: false, IsGameMaster: false, IsPartyLeader: false, IsInvisible: false,
            Direction: 90, ZoneId: 21, IsHidingHelmet: false, DisplayTitleId: 0, Visuals: visuals));

        packet.ResetOffset();
        packet.ReadSByteString();
        packet.ReadBytes(1 + 3 + 2 + 1 + 14 + 1 + 1 + 2 + 2 + 2 + 2 + 1 + 4);

        packet.ReadByte().Should().Be((byte)UserPoseState.Standing);
        packet.ReadByte().Should().Be((byte)PlayerAppearanceState.Normal);
        packet.ReadInt().Should().Be(UserInfoPacketWriter.NoTransform);
    }

    [Fact]
    public void AStateChangeCarriesAFourByteValue()
    {
        var packet = MovementPacketWriter.StateChange(
            70_001, (byte)StateChangeType.CombatStance, (int)CombatStanceState.Ready);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_STATE_CHANGE);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadByte().Should().Be((byte)StateChangeType.CombatStance);
        packet.ReadInt().Should().Be(
            (int)CombatStanceState.Ready,
            because: "the client reads this field as a dword whatever the state type is");
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void TheNearbySnapshotStanceUsesTheSameShapeAsAStateChange()
    {
        var broadcast = MovementPacketWriter.StateChange(
            70_001, (byte)StateChangeType.CombatStance, (int)CombatStanceState.Ready);
        var snapshot = VisibilityPacketWriter.CombatStance(
            70_001, (byte)StateChangeType.CombatStance, (byte)CombatStanceState.Ready);

        snapshot.GetData().Should().Equal(
            broadcast.GetData(),
            "a stance learned from the nearby snapshot must look identical to one learned live");
    }

    [Fact]
    public void ARegionUserListStopsAtTheCountTheClientAccepts()
    {
        var ids = Enumerable.Range(1, VisibilityPacketWriter.RegionListMax + 500).ToList();

        var packet = VisibilityPacketWriter.RegionChangeList(ids);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)RegionListStage.List);
        packet.ReadShort().Should().Be(
            VisibilityPacketWriter.RegionListMax,
            because: "a longer list is refused whole rather than truncated by the client");
        packet.RemainingBytes.Should().Be(4 * VisibilityPacketWriter.RegionListMax);
    }

    [Fact]
    public void ANpcRegionListStopsAtTheSameCount()
    {
        var ids = Enumerable.Range(1, VisibilityPacketWriter.RegionListMax + 500).ToList();

        var packet = VisibilityPacketWriter.NpcRegion(ids);
        packet.ResetOffset();

        packet.ReadShort().Should().Be(VisibilityPacketWriter.RegionListMax);
        packet.RemainingBytes.Should().Be(4 * VisibilityPacketWriter.RegionListMax);
    }
}
