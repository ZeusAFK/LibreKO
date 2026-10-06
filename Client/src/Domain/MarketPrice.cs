using System;
using System.Collections.Generic;
using System.Globalization;

namespace LibreKO.Domain;

public readonly record struct MarketPriceDay(long Average, long Max, long Min)
{
    public bool HasTrades => Average > 0;
}

public sealed record MarketPriceReply(int Result, int ItemId, IReadOnlyList<MarketPriceDay> Days, int Trades, DateTime LastUpdate);

public enum MarketPriceVerdict
{
    NoActivity,
    Low,
    Expensive,
}

public static class MarketPrice
{
    public const int NoHistory = 0;
    public const int History = 1;
    public const int TooSoon = 11;
    public const int NotPremium = 41;
    public const int DaysShown = 5;

    private const long SmallestTop = 10;
    private const long EmptySpread = 100;
    private const long LargestStep = 100_000_000;
    private const long StepDivisor = 10;
    private const long Thousand = 1_000;
    private const long Million = 1_000_000;
    private const long Billion = 1_000_000_000;

    public static (long Top, long Bottom) Scale(IReadOnlyList<MarketPriceDay> days)
    {
        long high = 0;
        long low = long.MaxValue;
        foreach (var day in days)
        {
            high = Math.Max(high, day.Max);
            if (day.Min > 0) low = Math.Min(low, day.Min);
        }
        if (low == long.MaxValue) low = 0;
        if (high < SmallestTop) return (SmallestTop, 0);

        long spread = high - low;
        if (spread == 0) spread = EmptySpread;
        long step = LargestStep;
        while (spread < step && step > StepDivisor) step /= StepDivisor;
        if (step <= StepDivisor) step = spread;

        long top = (high + step - 1) / step * step;
        long bottom = low - low % step;
        if (top == bottom)
        {
            top = top * 3 / 2;
            bottom /= 2;
        }
        return (top, bottom);
    }

    public static string AxisLabel(long value)
    {
        if (value >= Billion) return Compact(value, Billion, "B");
        if (value >= Million) return Compact(value, Million, "M");
        if (value >= Thousand) return Compact(value, Thousand, "K");
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Compact(long value, long unit, string suffix) =>
        ((double)value / unit).ToString("0.##", CultureInfo.InvariantCulture) + suffix;

    public static long MarketAverage(IReadOnlyList<MarketPriceDay> days)
    {
        long sum = 0;
        int count = 0;
        foreach (var day in days)
        {
            if (!day.HasTrades) continue;
            sum += day.Average;
            count++;
        }
        return count == 0 ? 0 : sum / count;
    }

    public static MarketPriceVerdict Judge(long price, long average) =>
        average <= 0 ? MarketPriceVerdict.NoActivity
        : price <= average ? MarketPriceVerdict.Low
        : MarketPriceVerdict.Expensive;
}
