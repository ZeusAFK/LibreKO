using System;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BorderWarStateTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TakingTheFragmentStopsTheAltarCountdown()
    {
        var war = new BorderWarState();
        war.AltarTimer(46, Now);
        war.FragmentTaken("Zeus", 1);
        Assert.Equal("Zeus", war.Carrier);
        Assert.Equal(0, war.AltarSecondsLeft(Now));
    }

    [Fact]
    public void TheAltarCountdownRunsOnTheClock()
    {
        var war = new BorderWarState();
        war.FragmentTaken("Zeus", 1);
        war.AltarTimer(46, Now);
        Assert.Equal("", war.Carrier);
        Assert.Equal(46, war.AltarSecondsLeft(Now));
        Assert.Equal(36, war.AltarSecondsLeft(Now.AddSeconds(10)));
        Assert.Equal(0, war.AltarSecondsLeft(Now.AddMinutes(5)));
    }

    [Fact]
    public void AZeroAltarTimerMeansTheAltarIsBack()
    {
        var war = new BorderWarState();
        war.AltarTimer(46, Now);
        war.AltarTimer(0, Now.AddSeconds(46));
        Assert.Null(war.AltarBackUtc);
    }

    [Fact]
    public void TheFinishKeepsTheScoresAndCountsDownToTheTripHome()
    {
        var war = new BorderWarState();
        war.SetScores(320, 510);
        war.FragmentTaken("Zeus", 1);
        war.Finish(2, 60, Now);
        Assert.True(war.Finished);
        Assert.Equal(2, war.Winner);
        Assert.Equal(510, war.ElmoradScore);
        Assert.Equal("", war.Carrier);
        Assert.Equal(30, war.HomeSecondsLeft(Now.AddSeconds(30)));
    }

    [Fact]
    public void ResetClearsEverything()
    {
        var war = new BorderWarState();
        war.SetScores(3, 4);
        war.Finish(0, 60, Now);
        war.Reset();
        Assert.False(war.Finished);
        Assert.Equal(0, war.KarusScore);
        Assert.Null(war.HomeUtc);
    }
}
