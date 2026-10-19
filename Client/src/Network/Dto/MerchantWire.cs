namespace LibreKO.Network;

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
    BuyOpen = 0x21,
    BuyInsert = 0x22,
    BuyList = 0x23,
    BuyBuy = 0x24,
    BuySold = 0x25,
    BuyBought = 0x26,
    BuyClose = 0x27,
    BuyRegionInsert = 0x28,
    OfficialList = 0x30,
    StallList = 0x31,
}

public enum MerchantInOut : byte
{
    StallsInView = 1,
    ShowcaseChanged = 2,
}

public enum MerchantOpenResult : short
{
    WhileMerchanting = -4,
    WhileTrading = -3,
    WhileDead = -2,
    Accepted = 1,
    UnderLevelled = 30,
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
}

public static class MerchantWire
{
    public const int StallSlots = 12;
    public const int StallDisplaySlots = 4;
    public const int StallDisplaySlotsPremium = 8;
    public const byte PremiumFlagMask = 7;
}

public readonly record struct MerchantItemAddReply(bool Succeeded, int ItemId, int Count, short Durability, int Price, int SourcePos, int DestinationPos);

public readonly record struct MerchantBuyReply(bool Succeeded, int ItemId, int Remaining, int MerchantSlot, int BuyerSlot);

public readonly record struct MerchantStallOpened(bool Succeeded, int OwnerId, string Advert, byte Flags, int[] ItemIds);
