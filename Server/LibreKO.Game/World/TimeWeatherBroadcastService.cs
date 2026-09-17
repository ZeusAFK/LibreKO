using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Gameplay;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public class TimeWeatherBroadcastService(
    SessionManager sessionManager,
    ILogger<TimeWeatherBroadcastService> logger) : BackgroundService
{
    private const int MinutesPerHour = 60;

    public const byte WeatherFine = (byte)Common.Enums.WeatherType.Fine;
    public const byte WeatherRain = (byte)Common.Enums.WeatherType.Rain;
    public const byte WeatherSnow = (byte)Common.Enums.WeatherType.Snow;
    public const byte WeatherLeaves = (byte)Common.Enums.WeatherType.Leaves;

    private const int SnowChance = 2;
    private const int RainChance = 7;
    private const int LeafChance = 12;
    private const int FineAmountFloor = 70;

    // A full in-game day/night cycle takes this many REAL seconds (4 hours), so game time runs 6x.
    public const int DayCycleSeconds = 4 * 3600;
    private const int MinutesPerDay = 24 * 60;

    private const int TimeIntervalSeconds = 10;     // frequent enough that clients sync fast + interpolate
    private const int WeatherIntervalSeconds = 3600;

    private int _minuteOffset;

    public int MinuteOfDay => Wrap(EpochMinuteOfDay + _minuteOffset);

    public byte WeatherType { get; set; } = WeatherFine;
    public ushort WeatherAmount { get; set; }
    // GM event-rate bonuses (server-wide, percent). Applied multiplicatively on top of base.
    public byte ExpEventAmount { get; set; }
    public byte CoinEventAmount { get; set; }
    public byte NpEventAmount { get; set; }
    public byte DropEventAmount { get; set; }

    public long ApplyExpBonus(long baseExp) =>
        ExpEventAmount == 0 ? baseExp : baseExp * (100 + ExpEventAmount) / 100;

    public int ApplyCoinBonus(int baseAmount) =>
        CoinEventAmount == 0 ? baseAmount : (int)((long)baseAmount * (100 + CoinEventAmount) / 100);

    public int ApplyNpBonus(int baseAmount) =>
        NpEventAmount == 0 || baseAmount <= 0
            ? baseAmount
            : (int)((long)baseAmount * (100 + NpEventAmount) / 100);

    public int ApplyDropBonus(int baseRate) =>
        DropEventAmount == 0 ? baseRate : (int)((long)baseRate * (100 + DropEventAmount) / 100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Time/Weather broadcast service started (time {TimeSec}s, weather {WeatherSec}s)",
            TimeIntervalSeconds, WeatherIntervalSeconds);

        var timeTask = RunTimeLoop(stoppingToken);
        var weatherTask = RunWeatherLoop(stoppingToken);
        await Task.WhenAll(timeTask, weatherTask);
    }

    private async Task RunTimeLoop(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TimeIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (sessionManager.OnlineCount == 0) continue;
                await sessionManager.BroadcastToAll(BuildCurrentTimePacket());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Time broadcast tick failed");
            }
        }
    }

    private async Task RunWeatherLoop(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(WeatherIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                RollWeather();
                if (sessionManager.OnlineCount == 0) continue;
                await BroadcastWeatherAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Weather broadcast tick failed");
            }
        }
    }

    public Packet BuildCurrentTimePacket() => BuildTimePacket(MinuteOfDay);

    public void SetTimeOfDay(int hour, int minute) =>
        _minuteOffset = Wrap(hour * 60 + minute - EpochMinuteOfDay);

    public static bool TryParseTimeOfDay(string text, out int hour, out int minute)
    {
        hour = 0;
        minute = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length > 2)
            return false;
        if (!int.TryParse(parts[0], out hour) || hour < 0 || hour > 23)
            return false;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out minute) || minute < 0 || minute > 59))
            return false;
        return true;
    }

    public Packet BuildCurrentWeatherPacket() => BuildWeatherPacket(WeatherType, WeatherAmount);

    public bool TrySetWeather(byte type, ushort amount)
    {
        WeatherType = type;
        WeatherAmount = amount;
        return true;
    }

    // Game time runs on a DayCycleSeconds-long loop (4 real hours = one full day/night), derived
    // from the UTC epoch so every client/restart agrees without persisting anything. `+time` shifts it
    // by a live offset, which therefore resets on restart.
    private static int EpochMinuteOfDay =>
        (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % DayCycleSeconds * MinutesPerDay / DayCycleSeconds);

    private static int Wrap(int minutes) => (minutes % MinutesPerDay + MinutesPerDay) % MinutesPerDay;

    private static Packet BuildTimePacket(int minuteOfDay)
    {
        var now = DateTime.UtcNow;
        var pkt = TimeWeatherPacketWriter.GameTime(
            (short)now.Year, (short)now.Month, (short)now.Day,
            (short)(minuteOfDay / MinutesPerHour), (short)(minuteOfDay % MinutesPerHour));
        return pkt;
    }

    public static Packet BuildWeatherPacket(byte type, ushort amount)
    {
        var pkt = TimeWeatherPacketWriter.Weather(type, amount);
        return pkt;
    }

    private void RollWeather()
    {
        var roll = Random.Shared.Next(0, 101);
        WeatherType = roll switch
        {
            < SnowChance => WeatherSnow,
            < RainChance => WeatherRain,
            < LeafChance => WeatherLeaves,
            _ => WeatherFine,
        };

        var amount = (ushort)Random.Shared.Next(0, 101);
        WeatherAmount = WeatherType == WeatherFine
            ? (ushort)(amount > FineAmountFloor ? amount / 2 : 0)
            : amount;
    }

    public Packet BuildWeatherPacketFor(byte zoneId)
        => IsAlwaysClear(zoneId)
            ? BuildWeatherPacket(WeatherFine, WeatherAmount)
            : BuildCurrentWeatherPacket();

    public async Task BroadcastWeatherAsync()
    {
        var real = BuildCurrentWeatherPacket();
        var clear = BuildWeatherPacket(WeatherFine, WeatherAmount);

        foreach (var session in sessionManager.GetAll())
        {
            var packet = IsAlwaysClear(session.ZoneId) ? clear : real;
            try { await session.Client.SendPacket(packet); }
            catch (Exception ex) { logger.LogDebug(ex, "Weather send failed for {Id}", session.CharacterId); }
        }
    }

    private static bool IsAlwaysClear(byte zoneId)
        => zoneId is (byte)ZoneId.DesperationAbyss or (byte)ZoneId.HellAbyss or (byte)ZoneId.Arena;
}
