using System;
using Godot;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class SoccerTests
{
    private static Packet Reply(Action<Packet> body)
    {
        var p = new Packet(GameOpcodes.GS_MINING);
        body(p);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void AGoalReadsKickerTeamsAndScores()
    {
        var goal = SoccerWire.ReadGoal(Reply(w =>
        {
            w.WriteInt(77);
            w.WriteByte(Soccer.TeamBlue);
            w.WriteByte(Soccer.TeamRed);
            w.WriteByte(2);
            w.WriteByte(1);
        }));

        Assert.Equal(new SoccerGoal(77, Soccer.TeamBlue, Soccer.TeamRed, 2, 1), goal);
    }

    [Fact]
    public void AShortGoalIsDropped()
    {
        Assert.Null(SoccerWire.ReadGoal(Reply(w => w.WriteInt(77))));
    }

    [Theory]
    [InlineData(600, "10:00")]
    [InlineData(59.9, "00:59")]
    [InlineData(0, "00:00")]
    [InlineData(-3, "00:00")]
    [InlineData(3605, "00:05")]
    public void TheClockShowsMinutesAndSeconds(double seconds, string text)
    {
        Assert.Equal(text, Soccer.Clock(seconds));
    }

    [Fact]
    public void AKickLandsPastTheBallAwayFromTheKicker()
    {
        var landing = Soccer.Landing(new Vector3(10, 0, 10), new Vector3(10, 0, 8), 60);

        Assert.Equal(10f, landing.X, 3);
        Assert.Equal(15.5f, landing.Z, 3);
    }

    [Theory]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, true, true, false, false)]
    [InlineData(false, true, true, true, true)]
    [InlineData(false, false, false, false, true)]
    public void ThePitchAllowsOnlyKicksAndSpeedBuffs(bool soccerSkill, bool onPitch, bool hasTeam, bool speedBuff, bool allowed)
    {
        Assert.Equal(allowed, Soccer.SkillAllowed(soccerSkill, onPitch, hasTeam, speedBuff));
    }

    [Theory]
    [InlineData(Soccer.TeamBlue, Soccer.WinText, true)]
    [InlineData(Soccer.TeamRed, Soccer.WinText, false)]
    [InlineData(0, Soccer.DrawText, true)]
    public void TheResultPutsTheWinnersScoreFirst(int winner, int text, bool blueFirst)
    {
        Assert.Equal((text, blueFirst), Soccer.Result(winner));
    }

    [Fact]
    public void AnOwnGoalIsTheKickersTeamGoal()
    {
        Assert.True(Soccer.IsOwnGoal(Soccer.TeamBlue, Soccer.TeamBlue));
        Assert.False(Soccer.IsOwnGoal(Soccer.TeamBlue, Soccer.TeamRed));
    }
}
