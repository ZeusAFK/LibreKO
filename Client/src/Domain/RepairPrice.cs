using System;

namespace LibreKO.Domain;

public static class RepairPrice
{
    private const double PriceOffset = 10.0;
    private const double PriceDivisor = 10000.0;
    private const double PriceExponent = 0.75;

    public static int Cost(int buyPrice, int maxDurability, int durability)
    {
        int missing = maxDurability - durability;
        if (missing <= 0 || maxDurability <= 0) return 0;
        double cost = ((buyPrice - PriceOffset) / PriceDivisor + Math.Pow(buyPrice, PriceExponent)) * missing / maxDurability;
        return cost < 0 ? 0 : (int)cost;
    }
}
