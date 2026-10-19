using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class UpgradeSessionTests
{
    private readonly UpgradeSession session = new();

    [Fact]
    public void AWatchedUpgradeIsRevealed()
    {
        session.Sent();
        Assert.True(session.Awaiting);
        Assert.Equal(UpgradeAnswerView.Reveal, session.Answered());
        Assert.False(session.Awaiting);
    }

    [Fact]
    public void ClosingAfterSendingKeepsTheUpgradeAndAppliesItQuietly()
    {
        session.Sent();
        session.Closed();
        Assert.True(session.Awaiting);
        Assert.Equal(UpgradeAnswerView.Quiet, session.Answered());
    }

    [Fact]
    public void ReopeningBeforeTheAnswerKeepsTheBenchBusyUntilItArrives()
    {
        session.Sent();
        session.Closed();
        Assert.True(session.Awaiting);
        Assert.False(session.CanSend);
        Assert.Equal(UpgradeAnswerView.Quiet, session.Answered());
        Assert.True(session.CanSend);
    }

    [Fact]
    public void AnAnswerNobodyWaitsForIsQuiet() => Assert.Equal(UpgradeAnswerView.Quiet, session.Answered());
}
