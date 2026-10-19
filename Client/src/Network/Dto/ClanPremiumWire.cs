namespace LibreKO.Network;

public enum ClanPremiumSubOpcode : byte
{
    Query = 1,
    StatusChange = 2,
}

public enum ClanPremiumState : byte
{
    Inactive = 0,
    Active = 1,
}
