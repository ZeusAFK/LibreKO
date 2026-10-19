using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class WeatherRulesTests
{
    private const int Moradon = 21;
    private const int BiFrost = 31;
    private const int Zone66 = 66;
    private const int UnderTheCastle = 86;

    [Theory]
    [InlineData(Moradon, WeatherRules.Rain, WeatherRules.Rain)]
    [InlineData(Moradon, WeatherRules.Snow, WeatherRules.Snow)]
    [InlineData(BiFrost, WeatherRules.Rain, WeatherRules.Fine)]
    [InlineData(Zone66, WeatherRules.Rain, WeatherRules.DrawnSunny)]
    public void ZonesDecideWhatIsShown(int zone, int type, int shown) =>
        Assert.Equal(shown, WeatherRules.Type(zone, type));

    [Fact]
    public void UnderTheCastleKeepsItsOwnSky() => Assert.True(WeatherRules.Ignored(UnderTheCastle));

    [Theory]
    [InlineData(WeatherRules.Fine, 5, 20)]
    [InlineData(WeatherRules.Fine, 150, 100)]
    [InlineData(WeatherRules.Rain, 95, 70)]
    public void AmountsAreClamped(int type, int amount, int clamped) =>
        Assert.Equal(clamped, WeatherRules.Amount(type, amount));

    [Fact]
    public void HeavierWeatherDimsTheSun()
    {
        Assert.Equal(1f, WeatherRules.RelativeLight(WeatherRules.Fine, WeatherRules.MinAmount), 3);
        Assert.True(WeatherRules.RelativeLight(WeatherRules.Rain, WeatherRules.OtherMaxAmount) < 0.6f);
        Assert.True(WeatherRules.RelativeLight(WeatherRules.Snow, WeatherRules.OtherMaxAmount) < 0.6f);
        Assert.Equal(0.25f, WeatherRules.Light(WeatherRules.Rain, 100), 3);
    }
}
