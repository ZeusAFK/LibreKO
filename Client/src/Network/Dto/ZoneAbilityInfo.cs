namespace LibreKO.Network;

public struct ZoneAbilityInfo
{
    public ZoneAbilityType ZoneType;
    public int Tariff;
    public bool CanTrade;
    public bool CanTalk;

    public bool IsPvp => ZoneType != ZoneAbilityType.Neutral;
    public bool NpcsAreTargets =>
        ZoneType is not (ZoneAbilityType.Neutral or ZoneAbilityType.Spectator or ZoneAbilityType.CaitharosArena or ZoneAbilityType.PvpNeutralNpcs);
    public bool IsSiege => ZoneType is ZoneAbilityType.SiegeType1 or ZoneAbilityType.SiegeType2 or ZoneAbilityType.SiegeDisabled;
    public bool IsArena => ZoneType == ZoneAbilityType.FreeForAll;

    public bool IsHostilePlayer(int myNation, int theirNation) => ZoneType switch
    {
        ZoneAbilityType.FreeForAll => true,
        ZoneAbilityType.Neutral or ZoneAbilityType.Spectator => false,
        _ => myNation != theirNation,
    };

    public string TypeLabel => ZoneType switch
    {
        ZoneAbilityType.Neutral => "Safe Zone",
        ZoneAbilityType.FreeForAll => "Arena — Free for All",
        ZoneAbilityType.SiegeType1 or ZoneAbilityType.SiegeType2 or ZoneAbilityType.SiegeDisabled => "Siege Zone",
        _ => "PK Zone",
    };
}
