using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class StatusLineTests
{
    private readonly StatusLine line = new() { Hint = "Right-click damaged gear to repair it" };

    [Fact]
    public void AStatusOutlivesAHintChange()
    {
        line.Show("Repaired Raptor(+8).", bad: false);
        line.Hint = "Right-click damaged gear to repair it";
        Assert.Equal("Repaired Raptor(+8).", line.Text);
        Assert.Equal(StatusTone.Good, line.Tone);
    }

    [Fact]
    public void ExpiringReturnsToTheLatestHint()
    {
        int token = line.Show("Repair failed.", bad: true);
        line.Hint = "Right-click a bag item to sell";
        Assert.True(line.Expire(token));
        Assert.Equal("Right-click a bag item to sell", line.Text);
        Assert.Equal(StatusTone.Hint, line.Tone);
    }

    [Fact]
    public void AnOlderTimerLeavesANewerStatus()
    {
        int first = line.Show("Sold Raptor(+8).", bad: false);
        int second = line.Show("Sold Water of Ibexs.", bad: false);
        Assert.False(line.Expire(first));
        Assert.Equal("Sold Water of Ibexs.", line.Text);
        Assert.True(line.Expire(second));
    }
}
