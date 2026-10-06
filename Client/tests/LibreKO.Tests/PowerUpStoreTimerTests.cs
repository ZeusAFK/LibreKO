using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PowerUpStoreTimerTests
{
    [Theory]
    [InlineData(2, 4, 30, "2 d 4 h")]
    [InlineData(3, 0, 10, "3 d")]
    [InlineData(0, 4, 48, "4 h 48 min")]
    [InlineData(0, 6, 0, "6 h")]
    [InlineData(0, 0, 35, "35 min")]
    public void TheLabelShowsTheTwoLargestUnits(int days, int hours, int minutes, string label) =>
        Assert.Equal(label, PowerUpStoreTimer.Label(new TimeSpan(days, hours, minutes, 20)));

    [Fact]
    public void UnderAMinuteReadsLessThanOne() =>
        Assert.Equal("< 1 min", PowerUpStoreTimer.Label(TimeSpan.FromSeconds(42)));

    [Fact]
    public void AnEndedDiscountHasNoLabel() =>
        Assert.Equal("", PowerUpStoreTimer.Label(TimeSpan.FromSeconds(-5)));
}
