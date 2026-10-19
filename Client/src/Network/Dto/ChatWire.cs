namespace LibreKO.Network;

public enum ChatTargetType : byte
{
    Whisper = 1,
    BlockToggle = 2,
    ClanAdmission = 4,
}

public enum ChatTargetResult : short
{
    SenderBlocked = -3,
    CrossNationZone = -2,
    TargetBlocked = -1,
    TargetNotFound = 0,
    Connected = 1,
}

public enum ChatAuthority : byte
{
    None = 0,
    Player = 1,
    GameMaster = 2,
}

public enum ChatConsultKind : byte
{
    Clan = 0,
    Player = 1,
}

public static class ChatWire
{
    public const byte TypeDeathNotice = 26;
    public const int MessageMaxLength = 128;

    private const int DeathNoticeMinBytes = 4 + 4 + 1 + 4 + 1 + 4;

    public static int? ReadKrowazSeconds(Packet p) => p.RemainingBytes >= 2 ? p.ReadShort() : null;

    public static DeathNotice? ReadDeathNotice(Packet p)
    {
        if (p.RemainingBytes < DeathNoticeMinBytes) return null;
        int killerNation = p.ReadByte();
        int victimNation = p.ReadByte();
        bool king = p.ReadByte() != 0;
        int kind = p.ReadByte();
        if (kind == DeathNotices.DefeatedKind) (killerNation, victimNation) = (victimNation, killerNation);
        int killerId = p.ReadInt();
        string killerName = p.ReadSByteString();
        if (p.RemainingBytes < 5) return null;
        int victimId = p.ReadInt();
        string victimName = p.ReadSByteString();
        int x = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
        int z = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
        return new DeathNotice(victimNation, killerNation, kind, killerId, killerName, victimId, victimName, x, z, king);
    }
}

public enum ChatType : byte
{
    General = 1,
    Private = 2,
    Party = 3,
    Forced = 4,
    Shout = 5,
    Clan = 6,
    Public = 7,
    WarSystem = 8,
    Permanent = 9,
    MonumentNotice = 11,
    GameMaster = 12,
    Command = 13,
    Merchant = 14,
    Alliance = 15,
    SeekingParty = 19,
    GameMasterInfo = 21,
    ClanOfficer = 23,
    ClanNotice = 24,
    KrowazNotice = 25,
    ChaosStoneNotice = 27,
    BoxItemNotice = 28,
    ChatRoom = 33,
    NoahKnights = 34,
    MarqueeNotice = 37,
    InfoNotice = 38,
    AlarmNotice = 39,
}
