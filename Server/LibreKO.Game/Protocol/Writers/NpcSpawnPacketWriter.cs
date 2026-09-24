using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum NpcSpawnKind : byte
{
    Monster = 1,
    Npc = 2,
}

public sealed class NpcSpawnPacketWriter
{
    public const byte InOutIn = 1;
    public const byte MoveWalking = 1;

    public const short NoClan = 0;
    public const short NoClanMarkVersion = 0;
    public const byte NoEventRoom = 0;

    public readonly record struct NpcState(
        int UniqueId,
        short NpcId,
        bool IsMonster,
        short ModelId,
        int SellingGroup,
        byte NpcType,
        short Size,
        int WeaponRight,
        int WeaponLeft,
        byte Nation,
        byte Level,
        short X,
        short Z,
        short Y,
        bool GateOpen,
        byte ObjectType,
        short Direction);

    private readonly byte _inOutType;
    private readonly NpcState? _npc;
    private readonly int _uniqueId;

    private NpcSpawnPacketWriter(byte inOutType, int uniqueId, NpcState? npc)
    {
        _inOutType = inOutType;
        _uniqueId = uniqueId;
        _npc = npc;
    }

    public static Packet In(byte inOutType, NpcState npc) => new NpcSpawnPacketWriter(inOutType, npc.UniqueId, npc).Build();
    public static Packet Out(byte inOutType, int uniqueId) => new NpcSpawnPacketWriter(inOutType, uniqueId, null).Build();
    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_NPC_INOUT);
        packet.WriteByte(_inOutType);
        packet.WriteInt(_uniqueId);

        if (_npc is { } npc)
            WriteRecord(packet, npc);

        return packet;
    }

    public static Packet Move(int uniqueId, short x, short z, short y, ushort moveRate)
    {
        var packet = new Packet(GameOpcodes.GS_NPC_MOVE);
        packet.WriteByte(MoveWalking);
        packet.WriteInt(uniqueId);
        packet.WriteShort(x);
        packet.WriteShort(z);
        packet.WriteShort(y);
        packet.WriteUShort(moveRate);
        return packet;
    }

    public static Packet GateFlag(
        byte inOutType, int uniqueId, short npcId, byte npcType, int maxHp, int hp, bool gateOpen)
    {
        var packet = new Packet(GameOpcodes.GS_NPC_INOUT);
        packet.WriteByte(inOutType);
        packet.WriteInt(uniqueId);
        packet.WriteShort(npcId);
        packet.WriteByte(npcType);
        packet.WriteInt(maxHp);
        packet.WriteInt(hp);
        packet.WriteByte((byte)(gateOpen ? 1 : 0));
        return packet;
    }

    public static void WriteRecord(Packet packet, NpcState npc)
    {
        packet.WriteShort(npc.NpcId);
        packet.WriteByte((byte)(npc.IsMonster ? NpcSpawnKind.Monster : NpcSpawnKind.Npc));
        packet.WriteShort(npc.ModelId);
        packet.WriteInt(npc.SellingGroup);
        packet.WriteByte(npc.NpcType);
        packet.WriteInt(0);
        packet.WriteShort(npc.Size);
        packet.WriteInt(npc.WeaponRight);
        packet.WriteInt(npc.WeaponLeft);
        packet.WriteByte(npc.IsMonster && npc.NpcType != NpcData.TypeGuardSummon ? (byte)0 : npc.Nation);
        packet.WriteByte(npc.Level);
        packet.WriteShort(npc.X);
        packet.WriteShort(npc.Z);
        packet.WriteShort(npc.Y);
        packet.WriteInt(npc.GateOpen ? 1 : 0);
        packet.WriteByte(npc.ObjectType);
        packet.WriteShort(NoClan);
        packet.WriteShort(NoClanMarkVersion);
        packet.WriteByte((byte)npc.Direction);
        packet.WriteByte(NoEventRoom);
    }
}
