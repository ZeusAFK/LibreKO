namespace LibreKO.Network;

public enum NameChangeSubOpcode : byte
{
    Character = 0,
    Clan = 16,
}

public enum NameChangeResult : byte
{
    ShowDialog = 1,
    Invalid = 2,
    Success = 3,
    InClan = 4,
    AuctionOpen = 11,
}

public enum ClanNameChangeResult : byte
{
    ShowDialog = 1,
    Invalid = 2,
    NotInClan = 4,
    Success = 16,
}
