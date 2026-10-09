namespace LibreKO.Domain;

public static class WarehouseRules
{
    public const int NonStorableRace = 73;
    public const int VaultTicketRaceFirst = 77;
    public const int VaultTicketRaceLast = 79;
    public const int NonStorableIdFirst = 900_000_000;
    public const int NonStorableIdLast = 999_999_999;
    public const int NoTradeIdFirst = 900_000_001;
    public const int NoTradeIdLast = 999_999_999;
    public const int NonStorableText = 42050;
    public const int CoinMax = 2_100_000_000;

    public static bool Storable(int itemId, int race) =>
        race != NonStorableRace
        && race is < VaultTicketRaceFirst or > VaultTicketRaceLast
        && itemId is < NonStorableIdFirst or > NonStorableIdLast;

    public static bool IsNoTradeId(int itemId) => itemId is >= NoTradeIdFirst and <= NoTradeIdLast;
}
