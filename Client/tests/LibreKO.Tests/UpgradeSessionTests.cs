using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class UpgradeSessionTests
{
    private readonly UpgradeSession _session = new();

    [Fact]
    public void AWatchedUpgradeIsRevealed()
    {
        _session.Sent();
        Assert.True(_session.Locked);
        Assert.Equal(UpgradeAnswerView.Reveal, _session.Answered());
        Assert.False(_session.Locked);
    }

    [Fact]
    public void ClosingAfterSendingKeepsTheUpgradeAndAppliesItQuietly()
    {
        _session.Sent();
        _session.Closed();
        Assert.True(_session.Awaiting);
        Assert.False(_session.Locked);
        Assert.Equal(UpgradeAnswerView.Quiet, _session.Answered());
    }

    [Fact]
    public void ReopeningBeforeTheAnswerFreesTheBenchButHoldsTheNextUpgrade()
    {
        _session.Sent();
        _session.Closed();
        Assert.False(_session.Locked);
        Assert.False(_session.CanSend);
        Assert.Equal(UpgradeAnswerView.Quiet, _session.Answered());
        Assert.True(_session.CanSend);
    }

    [Fact]
    public void AnAnswerNobodyWaitsForIsQuiet() => Assert.Equal(UpgradeAnswerView.Quiet, _session.Answered());
}
