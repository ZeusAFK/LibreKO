using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

internal static class ExchangePacketConstants
{
    public static bool IsWithinTradeRange(UserSession a, UserSession b)
    {
        if (a.ZoneId != b.ZoneId)
            return false;

        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz <= MaxTradeDistance * MaxTradeDistance;
    }

    public const byte ExchangeRequest = 1;
    public const byte ExchangeAgree = 2;
    public const byte ExchangeAdd = 3;
    public const byte ExchangeOtherAdd = 4;
    public const byte ExchangeDecide = 5;
    public const byte ExchangeOtherDecide = 6;
    public const byte ExchangeDone = 7;
    public const byte ExchangeCancel = 8;

    public const int ItemNoTrade = 900000001;
    public const int ItemNoTradeMax = 1_000_000_000;
    public const byte RaceUntradeable = 20;
    public const int CoinMax = 2_100_000_000;
    public const float MaxTradeDistance = 12f;
}
