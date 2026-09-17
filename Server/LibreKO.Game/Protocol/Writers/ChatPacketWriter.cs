using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ChatPacketWriter
{
    public const byte TypeGeneral = 1;
    public const byte TypePrivate = 2;
    public const byte TypeParty = 3;
    public const byte TypeNation = 4;
    public const byte TypeSystemNotice = 8;
    public const byte TypeGameMaster = 12;
    public const byte TypeDeathNotice = 26;

    public const byte AuthorityPlayer = 1;
    public const byte AuthorityGameMaster = 2;
    public const byte AuthorityNone = 0;

    public const int NoSender = -1;

    private byte _type;
    private byte _nation;
    private int _characterId;
    private string _name = string.Empty;
    private string _message = string.Empty;
    private byte _authority;

    public static Packet Say(
        byte type, byte nation, int characterId, string name, string message, bool isGameMaster) =>
        new ChatPacketWriter
        {
            _type = type,
            _nation = nation,
            _characterId = characterId,
            _name = name,
            _message = message,
            _authority = isGameMaster ? AuthorityGameMaster : AuthorityPlayer,
        }.Build();

    public static Packet SystemNotice(byte nation, string message) =>
        new ChatPacketWriter
        {
            _type = TypeSystemNotice,
            _nation = nation,
            _characterId = NoSender,
            _message = message,
        }.Build();

    public static Packet NationNotice(byte nation, int characterId, string name, string message) =>
        new ChatPacketWriter
        {
            _type = TypeSystemNotice,
            _nation = nation,
            _characterId = characterId,
            _name = name,
            _message = message,
            _authority = AuthorityNone,
        }.Build();

    public static Packet DeathNotice(
        byte victimNation, byte killerNation, byte noticeType,
        int killerId, string killerName, int victimId, string victimName,
        short victimX, short victimZ)
    {
        var packet = new Packet(GameOpcodes.GS_CHAT);
        packet.WriteByte(TypeDeathNotice);
        packet.WriteByte(victimNation);
        packet.WriteByte(killerNation);
        packet.WriteByte(0);
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
        packet.WriteByte(_type);
        packet.WriteByte(_nation);
        packet.WriteInt(_characterId);
        packet.WriteSByteString(_name);
        packet.WriteString(_message);

        if (_characterId >= 0)
            packet.WriteByte(_authority);

        return packet;
    }
}
