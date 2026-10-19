using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class KingCommandsTests
{
    private static string Names(int id, string fallback) => fallback;

    [Fact]
    public void TheRoyalOrderKeepsTheWholeMessage()
    {
        var command = KingCommands.Parse("royalorder", "Gather at the bridge", Names);
        Assert.Equal(KingCommandKind.RoyalOrder, command?.Kind);
        Assert.Equal("Gather at the bridge", command?.Text);
    }

    [Fact]
    public void APrizeNamesThePlayerAndTheCoins()
    {
        var command = KingCommands.Parse("Prize", "Rikka 50000", Names);
        Assert.Equal(KingCommandKind.Prize, command?.Kind);
        Assert.Equal("Rikka", command?.Target);
        Assert.Equal(50000, command?.Amount);
    }

    [Theory]
    [InlineData("Rain", (byte)KingWeather.Rain)]
    [InlineData("snow", (byte)KingWeather.Snow)]
    [InlineData("Clear", (byte)KingWeather.Clear)]
    public void WeatherCommandsCarryTheirType(string word, byte weather)
    {
        var command = KingCommands.Parse(word, "70", Names);
        Assert.Equal(KingCommandKind.Weather, command?.Kind);
        Assert.Equal(weather, command?.Weather);
        Assert.Equal(70, command?.Amount);
    }

    [Fact]
    public void OtherWordsAreNotKingCommands()
    {
        Assert.Null(KingCommands.Parse("town", "", Names));
    }

    [Theory]
    [InlineData((byte)KingEventSubOpcode.Experience, -1, KingCommands.OnlyKingsText)]
    [InlineData((byte)KingEventSubOpcode.DropRate, -3, KingCommands.TreasuryText)]
    [InlineData((byte)KingEventSubOpcode.Prize, -2, KingCommands.NoSuchIdText)]
    [InlineData((byte)KingEventSubOpcode.Prize, -4, KingCommands.TreasuryText)]
    [InlineData((byte)KingEventSubOpcode.Weather, -2, 0)]
    [InlineData((byte)KingEventSubOpcode.Experience, 1, 0)]
    public void ResultsMapToThe2619Texts(byte kingEvent, short result, int text)
    {
        Assert.Equal(text, KingCommands.ResultText((KingEventSubOpcode)kingEvent, result));
    }
}
