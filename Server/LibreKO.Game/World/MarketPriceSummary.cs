using LibreKO.Common.Domain.Entities;

namespace LibreKO.Game.World;

public readonly record struct MarketPriceDayView(long Average, long Max, long Min)
{
    public static readonly MarketPriceDayView Empty = new(0, 0, 0);
}

public sealed record MarketPriceSummary(IReadOnlyList<MarketPriceDayView> Days, int Trades, DateTime LastUpdate)
{
    public const int DaysShown = 5;

    public static MarketPriceSummary? Of(IEnumerable<MarketPriceDay> rows)
    {
        var latest = rows.Where(r => r.Trades > 0).OrderByDescending(r => r.Day).Take(DaysShown).ToList();
        if (latest.Count == 0)
            return null;

        return new MarketPriceSummary(
            latest.Select(r => new MarketPriceDayView(r.AveragePrice, r.MaxPrice, r.MinPrice)).ToList(),
            latest.Sum(r => r.Trades),
            latest.Max(r => r.LastTradeAt));
    }
}
