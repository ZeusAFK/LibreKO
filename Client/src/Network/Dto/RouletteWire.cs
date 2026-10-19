namespace LibreKO.Network;

public static class RouletteWire
{
    public const int PrizeLogMax = 20;
}

public readonly record struct RoulettePrize(int ItemId, int Quantity, int UnixTime);
