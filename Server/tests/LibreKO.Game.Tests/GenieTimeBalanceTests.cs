using LibreKO.Common.Domain.Entities;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class GenieTimeBalanceTests
{
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += (long)(seconds * TimestampFrequency);
    }

    [Fact]
    public void OfflineTimeDoesNotConsumeCreditAndOnlineTimeDoes()
    {
        var clock = new Clock();
        var balance = new GenieTimeBalance(clock);
        balance.Load(7200);
        clock.Advance(86400);
        Assert.Equal(7200, balance.RemainingSeconds);
        balance.Resume();
        clock.Advance(60);
        Assert.Equal(7140, balance.RemainingSeconds);
        balance.Pause();
        clock.Advance(86400);
        Assert.Equal(7140, balance.RemainingSeconds);
    }

    [Fact]
    public void RepeatedResumeAndSaveReadsDoNotResetElapsedTime()
    {
        var clock = new Clock();
        var balance = new GenieTimeBalance(clock);
        balance.Load(120);
        balance.Resume();
        clock.Advance(15.5);
        balance.Resume();
        Assert.Equal(104.5, balance.RemainingSeconds);
        clock.Advance(4.25);
        double saved = balance.RemainingSeconds;
        Assert.Equal(100.25, saved);
        var reconnect = new GenieTimeBalance(clock);
        reconnect.Load(saved);
        clock.Advance(3600);
        Assert.Equal(100.25, reconnect.RemainingSeconds);
        reconnect.Resume();
        clock.Advance(0.5);
        Assert.Equal(99.75, reconnect.RemainingSeconds);
    }

    [Fact]
    public void AddingSpiritPreservesTheRemainingFractionAndNeverGoesNegative()
    {
        var clock = new Clock();
        var balance = new GenieTimeBalance(clock);
        balance.Load(60);
        balance.Resume();
        clock.Advance(20.5);
        balance.AddSeconds(7200);
        Assert.Equal(7239.5, balance.RemainingSeconds);
        clock.Advance(8000);
        Assert.Equal(0, balance.RemainingSeconds);
        balance.Pause();
        balance.AddSeconds(60);
        clock.Advance(8000);
        Assert.Equal(60, balance.RemainingSeconds);
    }

    [Fact]
    public void PersistentBalanceTakesPrecedenceOverLegacyExpiry()
    {
        var character = new Character
        {
            GenieRemainingSeconds = 7200,
            GenieExpiry = DateTime.UtcNow.AddDays(-30)
        };
        Assert.Equal(120, character.GenieMinutes);
        Assert.Equal(2, character.GenieHours);
        character.GenieRemainingSeconds = 0;
        character.GenieExpiry = DateTime.UtcNow.AddDays(30);
        Assert.Equal(0, character.GenieMinutes);
    }
}
