using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class StatusLineTests
{
    private readonly StatusLine _line = new() { Hint = "Right-click damaged gear to repair it" };

    [Fact]
    public void AStatusOutlivesAHintChange()
    {
        _line.Show("Repaired Raptor(+8).", bad: false);
        _line.Hint = "Right-click damaged gear to repair it";
        Assert.Equal("Repaired Raptor(+8).", _line.Text);
        Assert.Equal(StatusTone.Good, _line.Tone);
    }

    [Fact]
    public void ExpiringReturnsToTheLatestHint()
    {
        int token = _line.Show("Repair failed.", bad: true);
        _line.Hint = "Right-click a bag item to sell";
        Assert.True(_line.Expire(token));
        Assert.Equal("Right-click a bag item to sell", _line.Text);
        Assert.Equal(StatusTone.Hint, _line.Tone);
    }

    [Fact]
    public void AnOlderTimerLeavesANewerStatus()
    {
        int first = _line.Show("Sold Raptor(+8).", bad: false);
        int second = _line.Show("Sold Water of Ibexs.", bad: false);
        Assert.False(_line.Expire(first));
        Assert.Equal("Sold Water of Ibexs.", _line.Text);
        Assert.True(_line.Expire(second));
    }
}
