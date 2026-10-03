using System;

namespace LibreKO.Domain;

public static class BuyLimit
{
    public static int Max(long wallet, int unitPrice, long? freeWeight, int unitWeight, int room)
    {
        long max = Math.Max(0, room);
        if (unitPrice > 0) max = Math.Min(max, Math.Max(0, wallet) / unitPrice);
        if (freeWeight is { } free && unitWeight > 0) max = Math.Min(max, Math.Max(0, free) / unitWeight);
        return (int)max;
    }
}
