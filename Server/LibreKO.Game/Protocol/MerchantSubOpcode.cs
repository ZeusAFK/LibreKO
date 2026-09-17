namespace LibreKO.Game.Protocol;

public enum MerchantSubOpcode : byte
{
    Open = 1,
    Close = 2,
    ItemAdd = 3,
    ItemCancel = 4,
    ItemList = 5,
    ItemBuy = 6,
    Insert = 7,
    TradeCancel = 8,
    ItemPurchased = 9,
    SellingStallRequest = 0x11,
    SellingStallOpen = 0x12,
    BuyOpen = 0x21,
    BuyInsert = 0x22,
    BuyList = 0x23,
    BuyBuy = 0x24,
    BuySold = 0x25,
    BuyBought = 0x26,
    BuyClose = 0x27,
    BuyRegionInsert = 0x28,
    BuyingStallRequest = 0x51,
    BuyingStallOpen = 0x52,
    StallList = 0x31,
}

public enum MerchantInOut : byte
{
    SessionEnded = 0,
    StallsInView = 1,
    StallClosed = 2,
}

public enum MerchantMode : sbyte
{
    None = -1,
    Selling = 0,
    Buying = 1,
}

public enum MerchantResult : short
{
    CannotTrade = -5,
    Refused = 0,
    Succeeded = 1,
}

public enum BuyingMerchantResult : byte
{
    Accepted = 1,
    WhileDead = 2,
    WhileMerchanting = 3,
    NotAllowedHere = 4,
    RegistrationFailed = 5,
    WrongItemSetup = 6,
    WrongStallSetup = 7,
    WrongPurchaseCount = 8,
    NoSuchItemWanted = 9,
    SellerFundsTooLow = 10,
    BuyerFundsTooLow = 11,
    ItemNotSellable = 13,
    InventoryFull = 15,
    OverMaxLimit = 16,
    NeedsRepair = 17,
    UnderLevelled = 18,
    TradeLockedForNow = 28,
}
