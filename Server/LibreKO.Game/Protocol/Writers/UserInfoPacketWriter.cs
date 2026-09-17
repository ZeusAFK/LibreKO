using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum PlayerAppearanceState : byte
{
    Hidden = 0,
    Normal = 1,
    Giant = 2,
    Dwarf = 3,
    Blinking = 4,
    GiantTarget = 6,
    ChaosNormal = 7,
}

public sealed class UserInfoPacketWriter
{
    public const short NoMarkVersion = -1;
    public const byte AuthorityPlayer = 1;
    public const byte AuthorityGameMaster = 0;
    public const byte NoKnightsRank = 0xFF;
    public const int NoTransform = 0;

    public readonly record struct ClanState(
        short AllianceId,
        string Name,
        byte Grade,
        byte Ranking,
        short CapeId,
        byte CapeR,
        byte CapeG,
        byte CapeB,
        byte Flag);

    public readonly record struct VisualItem(int ItemId, short Durability, byte Flag);

    public readonly record struct UserState(
        string Name,
        byte Nation,
        short KnightsId,
        byte KnightsFame,
        ClanState? Clan,
        ushort NoClanNationCode,
        byte Level,
        byte Race,
        short Class,
        short X,
        short Z,
        short Y,
        byte Face,
        int Hair,
        byte Pose,
        bool NeedParty,
        bool IsGameMaster,
        bool IsPartyLeader,
        bool IsInvisible,
        short Direction,
        ushort ZoneId,
        bool IsHidingHelmet,
        short DisplayTitleId,
        IReadOnlyList<VisualItem> Visuals);

    public static void WriteRecord(Packet packet, UserState user)
    {
        packet.WriteSByteString(user.Name);
        packet.WriteByte(user.Nation);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteShort(user.KnightsId > 0 ? user.KnightsId : (short)0);
        packet.WriteByte(user.KnightsFame);

        if (user.Clan is { } clan)
        {
            packet.WriteShort(clan.AllianceId);
            packet.WriteSByteString(clan.Name);
            packet.WriteByte(clan.Grade);
            packet.WriteByte(clan.Ranking);
            packet.WriteShort(NoMarkVersion);
            packet.WriteShort(clan.CapeId);
            packet.WriteByte(clan.CapeR);
            packet.WriteByte(clan.CapeG);
            packet.WriteByte(clan.CapeB);
            packet.WriteByte(0);
            packet.WriteByte(clan.Flag);
        }
        else
        {
            packet.WriteInt(0);
            packet.WriteShort(0);
            packet.WriteByte(0);
            packet.WriteUShort(user.NoClanNationCode);
            packet.WriteInt(0);
            packet.WriteByte(0);
        }

        packet.WriteByte(user.Level);
        packet.WriteByte(user.Race);
        packet.WriteShort(user.Class);
        packet.WriteShort(user.X);
        packet.WriteShort(user.Z);
        packet.WriteShort(user.Y);
        packet.WriteByte(user.Face);
        packet.WriteInt(user.Hair);
        packet.WriteByte(user.Pose);
        packet.WriteByte((byte)PlayerAppearanceState.Normal);
        packet.WriteInt(NoTransform);
        packet.WriteByte(user.NeedParty ? (byte)1 : (byte)0);
        packet.WriteByte(user.IsGameMaster ? AuthorityGameMaster : AuthorityPlayer);
        packet.WriteByte(user.IsPartyLeader ? (byte)1 : (byte)0);
        packet.WriteByte(user.IsInvisible ? (byte)1 : (byte)0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteShort(user.Direction);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteShort(0);
        packet.WriteByte(NoKnightsRank);
        packet.WriteByte(NoKnightsRank);

        foreach (var visual in user.Visuals)
        {
            packet.WriteInt(visual.ItemId);
            packet.WriteShort(visual.Durability);
            packet.WriteByte(visual.Flag);
        }

        packet.WriteUShort(user.ZoneId);
        packet.WriteInt(-1);
        packet.WriteByte(0);
        packet.WriteInt(0);
        packet.WriteByte(user.IsHidingHelmet ? (byte)1 : (byte)0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteShort(0);
        packet.WriteInt(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(1);
        packet.WriteUShort((ushort)user.DisplayTitleId);
    }

    public static Packet InOut(byte inOutType, int characterId, UserState? user)
    {
        var packet = new Packet(GameOpcodes.GS_USER_INOUT);
        packet.WriteByte(inOutType);
        packet.WriteByte(0);
        packet.WriteInt(characterId);

        if (user is { } state)
            WriteRecord(packet, state);

        return packet;
    }
}
