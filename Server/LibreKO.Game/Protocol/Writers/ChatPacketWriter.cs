using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ChatPacketWriter
{
    public const byte TypeGeneral = 1;
    public const byte TypePrivate = 2;
    public const byte TypeParty = 3;
    public const byte TypeNation = 4;
    public const byte TypeClan = 6;
    public const byte TypeSystemNotice = 8;
    public const byte TypeGameMaster = 12;
    public const byte TypeDeathNotice = 26;

    public const byte AuthorityPlayer = 1;
    public const byte AuthorityGameMaster = 2;
    public const byte AuthorityNone = 0;

    public const int NoSender = -1;

    private byte type;
    private byte nation;
    private int characterId;
    private string name = string.Empty;
    private string message = string.Empty;
    private byte authority;

    public static Packet Say(
        byte type, byte nation, int characterId, string name, string message, bool isGameMaster) =>
        new ChatPacketWriter
        {
            type = type,
            nation = nation,
            characterId = characterId,
            name = name,
            message = message,
            authority = isGameMaster ? AuthorityGameMaster : AuthorityPlayer,
        }.Build();

    public static Packet SystemNotice(byte nation, string message) =>
        new ChatPacketWriter
        {
            type = TypeSystemNotice,
            nation = nation,
            characterId = NoSender,
            message = message,
        }.Build();

    public static Packet ClanNotice(string message) =>
        new ChatPacketWriter
        {
            type = TypeClan,
            characterId = NoSender,
            message = message,
        }.Build();

    public static Packet NationNotice(byte nation, int characterId, string name, string message) =>
        new ChatPacketWriter
        {
            type = TypeSystemNotice,
            nation = nation,
            characterId = characterId,
            name = name,
            message = message,
            authority = AuthorityNone,
        }.Build();

    public static Packet DeathNotice(
        byte killerNation, byte victimNation, bool victimIsKing, byte noticeType,
        int killerId, string killerName, int victimId, string victimName,
        short victimX, short victimZ)
    {
        var packet = new Packet(GameOpcodes.GS_CHAT);
        packet.WriteByte(TypeDeathNotice);
        packet.WriteByte(killerNation);
        packet.WriteByte(victimNation);
        packet.WriteByte(victimIsKing ? (byte)1 : (byte)0);
        packet.WriteByte(noticeType);
        packet.WriteInt(killerId);
        packet.WriteSByteString(killerName);
        packet.WriteInt(victimId);
        packet.WriteSByteString(victimName);
        packet.WriteShort(victimX);
        packet.WriteShort(victimZ);
        return packet;
    }

    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_CHAT);
        packet.WriteByte(type);
        packet.WriteByte(nation);
        packet.WriteInt(characterId);
        packet.WriteSByteString(name);
        packet.WriteString(message);

        if (characterId >= 0)
            packet.WriteByte(authority);

        return packet;
    }
}
