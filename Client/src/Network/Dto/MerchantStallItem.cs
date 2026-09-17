namespace LibreKO.Network;

public struct MerchantStallItem
{
    public int ItemId;
    public int Count;
    public short Durability;
    public int Price;
    public bool IsEmpty => ItemId == 0;
}

public struct MerchantWishItem
{
    public int ItemId;
    public int Count;
    public int Price;
    public bool IsEmpty => ItemId == 0;
}

public struct StallOwner
{
    public int CharacterId;
    public bool IsBuying;
    public byte Flags;
    public bool IsPremium => (Flags & Net.MerchantPremiumFlagMask) != 0;
}
