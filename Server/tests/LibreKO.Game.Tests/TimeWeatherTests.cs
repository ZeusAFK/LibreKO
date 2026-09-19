using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class TimeWeatherTests
{
    [Fact]
    public void BuildCurrentTimePacket_ReportsCurrentGameTime()
    {
        var service = Create();

        var time = service.BuildCurrentTimePacket();
        var packetDate = new DateTime(time.ReadShort(), time.ReadShort(), time.ReadShort());

        packetDate.Should().BeOneOf(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(-1));
        time.ReadShort().Should().BeInRange((short)0, (short)23);
        time.ReadShort().Should().BeInRange((short)0, (short)59);
    }

    [Theory]
    [InlineData(18, 0)]
    [InlineData(0, 0)]
    [InlineData(23, 59)]
    public void SetTimeOfDay_MovesTheBroadcastClockToThatTime(int hour, int minute)
    {
        var service = Create();

        service.SetTimeOfDay(hour, minute);

        service.MinuteOfDay.Should().Be(hour * 60 + minute);
        var time = service.BuildCurrentTimePacket();
        time.ReadShort();
        time.ReadShort();
        time.ReadShort();
        time.ReadShort().Should().Be((short)hour);
        time.ReadShort().Should().Be((short)minute);
    }

    [Theory]
    [InlineData("18", 18, 0)]
    [InlineData("0", 0, 0)]
    [InlineData("18:20", 18, 20)]
    [InlineData(" 6 : 05 ", 6, 5)]
    public void TryParseTimeOfDay_AcceptsHourAndHourMinute(string text, int hour, int minute)
    {
        TimeWeatherBroadcastService.TryParseTimeOfDay(text, out var h, out var m).Should().BeTrue();
        h.Should().Be(hour);
        m.Should().Be(minute);
    }

    [Theory]
    [InlineData("")]
    [InlineData("24")]
    [InlineData("-1")]
    [InlineData("18:60")]
    [InlineData("18:20:30")]
    [InlineData("dusk")]
    public void TryParseTimeOfDay_RejectsAnythingElse(string text)
    {
        TimeWeatherBroadcastService.TryParseTimeOfDay(text, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TrySetWeather_RoundTripsThroughTheWeatherPacket()
    {
        var service = Create();

        service.TrySetWeather(TimeWeatherBroadcastService.WeatherSnow, 80).Should().BeTrue();

        var weather = service.BuildCurrentWeatherPacket();
        weather.ReadByte().Should().Be(TimeWeatherBroadcastService.WeatherSnow);
        weather.ReadUShort().Should().Be(80);
    }

    [Fact]
    public void DefaultWeather_IsClear()
    {
        var weather = Create().BuildCurrentWeatherPacket();

        weather.ReadByte().Should().Be(TimeWeatherBroadcastService.WeatherFine);
        weather.ReadUShort().Should().Be(0);
    }

    private static TimeWeatherBroadcastService Create() =>
        new(
            new SessionManager(),
            Substitute.For<ILogger<TimeWeatherBroadcastService>>());
}
