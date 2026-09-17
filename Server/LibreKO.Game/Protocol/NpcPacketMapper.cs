using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

internal static class NpcPacketMapper
{
    public static Packet BuildInOutPacket(NpcInstance npc, InOutType type) =>
        type == InOutType.Out
            ? NpcSpawnPacketWriter.Out((byte)type, npc.UniqueId)
            : NpcSpawnPacketWriter.In((byte)type, StateOf(npc));

    public static void WriteNpcInfo(Packet packet, NpcInstance npc) =>
        NpcSpawnPacketWriter.WriteRecord(packet, StateOf(npc));

    private static NpcSpawnPacketWriter.NpcState StateOf(NpcInstance npc) => new(
        npc.UniqueId,
        (short)npc.NpcId,
        npc.IsMonster,
        npc.ModelId,
        npc.SellingGroup,
        (byte)npc.NpcType,
        npc.Size,
        npc.WeaponType1,
        npc.WeaponType2,
        (byte)npc.Nation,
        (byte)npc.Level,
        npc.GetPosX,
        npc.GetPosZ,
        npc.GetPosY,
        npc.GateOpen,
        npc.ObjectType,
        npc.Direction);
}
