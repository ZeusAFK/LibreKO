namespace LibreKO.Network;

public enum ZoneAbilitySubOpcode : byte
{
    Update = 1,
    UserStatus = 2,
}

public enum UserStatusChange : byte
{
    Cured = 0,
    Inflicted = 1,
}

public enum ZoneAbilityType : byte
{
    Neutral = 0,
    Pvp = 1,
    Spectator = 2,
    SiegeType1 = 3,
    SiegeType2 = 4,
    FreeForAll = 5,
    SiegeDisabled = 6,
    CaitharosArena = 7,
    PvpNeutralNpcs = 8,
    TeamBattle = 9,
}
