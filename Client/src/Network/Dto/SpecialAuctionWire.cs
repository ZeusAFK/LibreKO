namespace LibreKO.Network;

public enum SpecialAuctionSubOpcode : byte
{
    Today = 1,
    Bid = 2,
    Collect = 3,
    MyInfo = 4,
    Notice = 5,
    Claim = 6,
    Log = 7,
}

public enum SpecialAuctionNoticeKind : byte
{
    Miscarried = 0,
    Ended = 1,
}

public enum AuctionBidStatus : byte
{
    Top = 1,
    Outbid = 2,
    Won = 3,
    Cancelled = 4,
}
