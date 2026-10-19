using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class SessionCountdownTests
{
    [Fact]
    public void TheChaosFinishCountsDownOnTheClockItIsGiven()
    {
        var chaos = new ChaosSession();
        chaos.Finish(10_000, 10);
        Assert.True(chaos.Finished);
        Assert.Equal(7, chaos.FinishSecondsLeft(3_000));
        Assert.Equal(0, chaos.FinishSecondsLeft(12_000));

        chaos.ResetBattle();
        Assert.False(chaos.Finished);
        Assert.Equal(0, chaos.FinishSecondsLeft(0));
    }

    [Fact]
    public void TheDrakiTimerRoundsUpToTheNextSecond()
    {
        var bifrost = new BifrostSession();
        bifrost.ApplyDrakiTimer(1, 1, 10, 0);
        Assert.Equal(8, bifrost.DrakiSecondsLeft(2_500));
        Assert.Equal(1, bifrost.DrakiSecondsLeft(9_999));
        Assert.Equal(0, bifrost.DrakiSecondsLeft(10_000));
    }

    [Fact]
    public void TheRoyaleGasCountsDownOnlyWhileTheCountdownRuns()
    {
        var royale = new KnightRoyaleSession();
        Assert.Equal(0, royale.NextGasSeconds(50_000));

        royale.ApplyGas(0, 1, 40_000);
        Assert.Equal(50, royale.NextGasSeconds(50_000));
        Assert.Equal(1, royale.NextGasSeconds(99_500));
        Assert.Equal(0, royale.NextGasSeconds(101_000));
    }
}
