namespace LibreKO.Network;

public struct SiegeCastleOwner
{
    public int ClanId;
    public int Mark;
    public byte Flag;
    public byte Grade;

    public readonly bool HasOwner => ClanId > 0;
}

public struct SiegeSchedule
{
    public int CastleIndex;
    public int SiegeType;
    public byte WarDay;
    public byte WarHour;
    public byte WarMinute;

    public readonly bool Scheduled => WarDay != 0 || WarHour != 0 || WarMinute != 0;
}

public struct SiegeMasterInfo
{
    public int CastleIndex;
    public string ClanName;
    public byte Nation;
    public int Members;
    public byte RequestDay;
    public byte RequestHour;
    public byte RequestMinute;
}

public struct SiegeStatus
{
    public int CastleIndex;
    public byte SiegeType;
    public string ClanName;
    public byte Nation;
    public int Members;
}
