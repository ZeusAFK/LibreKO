namespace LibreKO.Network;

public enum KnightsSubOpcode : byte
{
    Create = 0x01,
    Join = 0x02,
    Withdraw = 0x03,
    Remove = 0x04,
    Destroy = 0x05,
    Admit = 0x06,
    Reject = 0x07,
    Chief = 0x09,
    Vicechief = 0x0A,
    Officer = 0x0B,
    AllListRequest = 0x0C,
    MemberRequest = 0x0D,
    ModifyFame = 0x10,
    Invite = 0x11,
    ClanBattleChallenge = 0x13,
    ClanBattleAnswer = 0x14,
    ClanBattleSurrender = 0x15,
    ClanBattleBegun = 0x16,
    ClanBattleEnd = 0x17,
    MarkRegister = 0x1A,
    CapeNpc = 0x1B,
    AllyCreate = 0x1C,
    AllyReq = 0x1D,
    AllyInsert = 0x1E,
    AllyRemove = 0x1F,
    AllyPunish = 0x20,
    AllyList = 0x22,
    MarkReq = 0x23,
    Update = 0x24,
    MemberOnline = 0x27,
    MemberOffline = 0x28,
    PointRequest = 0x3B,
    PointMethod = 0x3C,
    DonatePoints = 0x3D,
    HandoverList = 0x3E,
    HandoverReq = 0x3F,
    DonationList = 0x40,
    UpdateNotice = 0x50,
    NoticeResult = 0x51,
    ClanAdvance = 0x52,
    ClanAdvanceApply = 0x54,
    ClanAdvanceRegister = 0x55,
    ClanAdvancePromote = 0x56,
    UpdateMemo = 0x58,
    KnightsPair = 96,
    WishBoard = 0x61,
    ClanBonus = 0x62,
    Top10 = 0x63,
    LeaderPoints = 0x64,
}

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

public readonly record struct EntityClanChange(int CharId, int ClanId, string Name, byte Grade, byte Ranking, byte Fame, int CapeId, int Colour);

public readonly record struct ClanCapeChange(int ClanId, int CapeId, int R, int G, int B);
