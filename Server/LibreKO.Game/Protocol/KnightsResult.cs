namespace LibreKO.Game.Protocol;

public enum KnightsResult : byte
{
    Succeeded = 1,
    NoSuchUser = 2,
    UserIsDead = 3,
    DifferentNation = 4,
    AlreadyInClan = 5,
    NoAuthority = 6,
    ClanNotValid = 7,
    ClanFull = 8,
    CannotChooseYourself = 9,
    NotInClan = 10,
    UserDeclined = 11,
    NotInThisZone = 12,
}

public enum KnightsCreateResult : byte
{
    TryAgainLater = 0,
    Succeeded = 1,
    LevelTooLow = 2,
    NameRefused = 3,
    NotEnoughCoins = 4,
    AlreadyInClan = 5,
    ClosedToday = 7,
    WrongServerGroup = 8,
    WrongTown = 9,
}

public enum KnightsNoticeResult : byte
{
    NoAuthority = 1,
    CommandUnavailable = 2,
    BadCharacterName = 3,
}

public enum KnightsDonateResult : byte
{
    Succeeded = 1,
    Failed = 2,
    ClanNotAccredited = 6,
    ClanNotValid = 7,
    NotEnoughPoints = 8,
}

public enum KnightsPointMethodResult : byte
{
    Succeeded = 1,
    NotSet = 2,
    ClanNotAccredited = 6,
}

public enum KnightsHandoverResult : byte
{
    Succeeded = 1,
    NotViceChief = 3,
    NoAuthority = 4,
}

public enum CapeResult : short
{
    Changed = 1,
    NotAllowed = -1,
    ClanNotFound = -2,
    NotSoldHere = -5,
    RankTooLow = -6,
    NotEnoughCoins = -7,
    MissingPurchaseItem = -8,
    NotEnoughClanPoints = -9,
    CastellanCannotUse = -10,
}
