using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol.Writers;

public enum RegionListStage : byte
{
    Clear = 0,
    List = 1,
    End = 2,
}

public sealed class VisibilityPacketWriter
{
    public const short BottomUserOnline = 1;
    public const short BottomUserTrailingFlag = 1;

    public const int RegionListMax = 999;

    public static Packet RegionChangeClear() =>
        Sub(GameOpcodes.GS_REGIONCHANGE, (byte)RegionListStage.Clear);

    public static Packet RegionChangeEnd() =>
        Sub(GameOpcodes.GS_REGIONCHANGE, (byte)RegionListStage.End);

    public static Packet RegionChangeList(IReadOnlyCollection<int> characterIds)
    {
        var listed = characterIds.Take(RegionListMax).ToList();
        var packet = Sub(GameOpcodes.GS_REGIONCHANGE, (byte)RegionListStage.List);
        packet.WriteShort((short)listed.Count);
        foreach (var id in listed)
            packet.WriteInt(id);
        return packet;
    }

    public static Packet UserOut(int characterId)
    {
        var packet = new Packet(GameOpcodes.GS_USER_INOUT);
        packet.WriteByte((byte)InOutType.Out);
        packet.WriteByte(0);
        packet.WriteInt(characterId);
        return packet;
    }

    public static Packet LookChange(int characterId, byte slot, int itemId, ushort durability)
    {
        var packet = new Packet(GameOpcodes.GS_USERLOOK_CHANGE);
        packet.WriteInt(characterId);
        packet.WriteByte(slot);
        packet.WriteInt(itemId);
        packet.WriteUShort(durability);
        packet.WriteByte(0);
        return packet;
    }

    public static Packet NpcRegion(IReadOnlyCollection<int> npcIds)
    {
        var listed = npcIds.Take(RegionListMax).ToList();
        var packet = new Packet(GameOpcodes.GS_NPC_REGION);
        packet.WriteShort((short)listed.Count);
        foreach (var id in listed)
            packet.WriteInt(id);
        return packet;
    }

    public static Packet BottomUserListHeader(byte sub, short zoneId, short count)
    {
        var packet = new Packet(GameOpcodes.GS_USER_INFO);
        packet.WriteByte(sub);
        packet.WriteByte(1);
        packet.WriteShort(zoneId);
        packet.WriteByte(0);
        packet.WriteShort(count);
        return packet;
    }

    public readonly record struct BottomUserEntry(
        string Name, byte Nation, short X, short Z, int ClanId);

    public static Packet BottomUserList(
        byte sub, short zoneId, IReadOnlyCollection<BottomUserEntry> users)
    {
        var packet = BottomUserListHeader(sub, zoneId, (short)users.Count);

        foreach (var user in users)
        {
            packet.WriteSByteString(user.Name);
            packet.WriteByte(user.Nation);
            packet.WriteShort(BottomUserOnline);
            packet.WriteShort(user.X);
            packet.WriteShort(user.Z);
            packet.WriteInt(user.ClanId);
            packet.WriteShort(0);
            packet.WriteShort(BottomUserTrailingFlag);
        }

        return packet;
    }

    public static void BeginUserRecord(Packet packet, int characterId)
    {
        packet.WriteByte(0);
        packet.WriteInt(characterId);
    }

    public static void BeginNpcRecord(Packet packet, int npcUniqueId) =>
        packet.WriteInt(npcUniqueId);

    public static Packet CombatStance(int characterId, byte stateType, byte stance)
    {
        var packet = new Packet(GameOpcodes.GS_STATE_CHANGE);
        packet.WriteInt(characterId);
        packet.WriteByte(stateType);
        packet.WriteInt(stance);
        return packet;
    }

    public static Packet SnapshotHeader(GameOpcodes opcode, short count)
    {
        var packet = new Packet(opcode);
        packet.WriteShort(count);
        return packet;
    }

    private static Packet Sub(GameOpcodes opcode, byte sub)
    {
        var packet = new Packet(opcode);
        packet.WriteByte(sub);
        return packet;
    }
}
