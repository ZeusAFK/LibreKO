namespace LibreKO.Network;

public struct ZoneAbilityInfo
{
    public const byte Neutral = 0;
    public const byte Pvp = 1;
    public const byte Spectator = 2;
    public const byte SiegeType1 = 3;
    public const byte SiegeType2 = 4;
    public const byte FreeForAll = 5;
    public const byte SiegeDisabled = 6;
    public const byte CaitharosArena = 7;
    public const byte PvpNeutralNpcs = 8;
    public const byte TeamBattle = 9;

    public byte ZoneType;
    public int Tariff;
    public bool CanTrade;
    public bool CanTalk;

    public bool IsPvp => ZoneType != Neutral;
    public bool NpcsAreTargets =>
        ZoneType is not (Neutral or Spectator or CaitharosArena or PvpNeutralNpcs);
    public bool IsSiege => ZoneType is SiegeType1 or SiegeType2 or SiegeDisabled;
    public bool IsArena => ZoneType == FreeForAll;

    public bool IsHostilePlayer(int myNation, int theirNation) => ZoneType switch
    {
        FreeForAll => true,
        Neutral or Spectator => false,
        _ => myNation != theirNation,
    };

    public string TypeLabel => ZoneType switch
    {
        Neutral => "Safe Zone",
        FreeForAll => "Arena — Free for All",
        SiegeType1 or SiegeType2 or SiegeDisabled => "Siege Zone",
        _ => "PK Zone",
    };
}
