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
    public const int PetRecordTail = 0;

    public readonly record struct StatueLook(
        string Name,
        byte Race,
        short Class,
        byte Face,
        int Hair,
        int Head,
        int Breast,
        int Leg,
        int Glove,
        int Foot,
        int RightHand,
        int LeftHand);

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
        int GateOpen,
        byte ObjectType,
        short Direction,
        string PetOwnerName = "",
        string PetName = "",
        StatueLook? Statue = null);

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
        if (npc.NpcType == NpcData.TypePet)
        {
            packet.WriteSByteString(npc.PetOwnerName);
            packet.WriteSByteString(npc.PetName);
            packet.WriteInt(PetRecordTail);
        }
        packet.WriteByte(npc.IsMonster && npc.NpcType != NpcData.TypeGuardSummon ? (byte)0 : npc.Nation);
        packet.WriteByte(npc.Level);
        packet.WriteShort(npc.X);
        packet.WriteShort(npc.Z);
        packet.WriteShort(npc.Y);
        packet.WriteInt(npc.GateOpen);
        packet.WriteByte(npc.ObjectType);
        packet.WriteShort(NoClan);
        packet.WriteShort(NoClanMarkVersion);
        packet.WriteByte((byte)npc.Direction);
        packet.WriteByte(NoEventRoom);
        if (IsRankerStatue(npc.NpcType))
            WriteStatue(packet, npc.Statue);
    }

    public static bool IsRankerStatue(int npcType) =>
        npcType >= NpcData.TypeRankerKarusFirst && npcType < NpcData.TypeRankerElMoradFirst + NpcData.RankerPlaces;

    private static void WriteStatue(Packet packet, StatueLook? statue)
    {
        if (statue is not { } look)
        {
            packet.WriteString(string.Empty);
            return;
        }
        packet.WriteString(look.Name);
        packet.WriteByte(look.Race);
        packet.WriteShort(look.Class);
        packet.WriteByte(look.Face);
        packet.WriteInt(look.Hair);
        packet.WriteInt(look.Head);
        packet.WriteInt(look.Breast);
        packet.WriteInt(look.Leg);
        packet.WriteInt(look.Glove);
        packet.WriteInt(look.Foot);
        packet.WriteInt(look.RightHand);
        packet.WriteInt(look.LeftHand);
    }
}
