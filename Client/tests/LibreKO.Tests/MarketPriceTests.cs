using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class MarketPriceTests
{
    private static MarketPriceDay[] Days(params (long Avg, long Max, long Min)[] days)
    {
        var all = new MarketPriceDay[MarketPrice.DaysShown];
        for (int i = 0; i < all.Length; i++)
            all[i] = i < days.Length ? new MarketPriceDay(days[i].Avg, days[i].Max, days[i].Min) : default;
        return all;
    }

    [Fact]
    public void TheAxisRoundsToTheStepOfTheSpread()
    {
        var scale = MarketPrice.Scale(Days((175, 200, 100), (150, 160, 140)));

        Assert.Equal(200, scale.Top);
        Assert.Equal(100, scale.Bottom);
    }

    [Fact]
    public void ASinglePriceIsCentredOnTheAxis()
    {
        var scale = MarketPrice.Scale(Days((5000, 5000, 5000)));

        Assert.Equal(7500, scale.Top);
        Assert.Equal(2500, scale.Bottom);
    }

    [Fact]
    public void ANarrowSpreadUsesTheSpreadAsItsStep()
    {
        var scale = MarketPrice.Scale(Days((120, 140, 90)));

        Assert.Equal(150, scale.Top);
        Assert.Equal(50, scale.Bottom);
    }

    [Fact]
    public void TinyPricesKeepTheDefaultAxis()
    {
        var scale = MarketPrice.Scale(Days((3, 5, 1)));

        Assert.Equal(10, scale.Top);
        Assert.Equal(0, scale.Bottom);
    }

    [Fact]
    public void AnEmptyDayDoesNotPullTheBottomToZero()
    {
        var scale = MarketPrice.Scale(Days((1_500_000, 2_000_000, 1_200_000), (0, 0, 0), (1_400_000, 1_600_000, 1_300_000)));

        Assert.Equal(2_000_000, scale.Top);
        Assert.Equal(1_200_000, scale.Bottom);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(15_000, "15K")]
    [InlineData(2_500_000, "2.5M")]
    [InlineData(1_234_567_890, "1.23B")]
    public void AxisLabelsAreCompact(long value, string expected)
    {
        Assert.Equal(expected, MarketPrice.AxisLabel(value));
    }

    [Fact]
    public void TheMarketAverageSkipsDaysWithoutTrades()
    {
        Assert.Equal(150, MarketPrice.MarketAverage(Days((100, 100, 100), (0, 0, 0), (200, 200, 200))));
        Assert.Equal(0, MarketPrice.MarketAverage(Days()));
    }

    [Theory]
    [InlineData(90, 100, MarketPriceVerdict.Low)]
    [InlineData(100, 100, MarketPriceVerdict.Low)]
    [InlineData(110, 100, MarketPriceVerdict.Expensive)]
    [InlineData(110, 0, MarketPriceVerdict.NoActivity)]
    public void AStallPriceIsJudgedAgainstTheAverage(long price, long average, MarketPriceVerdict expected)
    {
        Assert.Equal(expected, MarketPrice.Judge(price, average));
    }

    [Fact]
    public void ADayShowsOnlyWhenItHasAnAverage()
    {
        Assert.True(new MarketPriceDay(10, 12, 8).HasTrades);
        Assert.False(default(MarketPriceDay).HasTrades);
    }
}
