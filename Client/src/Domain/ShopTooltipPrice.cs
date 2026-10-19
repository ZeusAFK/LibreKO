namespace LibreKO.Domain;

public readonly record struct ShopPricing(bool LoyaltyShop, long Wallet);

public static class ShopTooltipPrice
{
    public const int PurchasingPriceText = 4523;
    public const int SellingPriceText = 4552;
    private const int Percent = 100;

    public static long Purchase(int buyPrice, int saleType, int tariff, bool loyaltyShop) =>
        loyaltyShop || tariff <= 0 || saleType == ItemData.SaleTypeFull
            ? buyPrice
            : buyPrice + (long)buyPrice * tariff / Percent;
}
