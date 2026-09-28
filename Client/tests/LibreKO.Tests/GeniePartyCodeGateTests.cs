using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class GeniePartyCodeGateTests
{
    private static GeniePartyCodeGate Enabled()
    {
        var gate = new GeniePartyCodeGate();
        gate.Configure(true, "PT123");
        return gate;
    }

    [Fact]
    public void CorrectPrivateMessageAuthorizesItsSenderOnlyOnce()
    {
        var gate = Enabled();
        Assert.True(gate.Receive(2, 42, "Alice", " PT123 ", 1, 100));
        Assert.False(gate.TryAccept(43, "Alice", 101));
        Assert.False(gate.TryAccept(42, "Bob", 101));
        Assert.True(gate.TryAccept(42, "ALICE", 101));
        Assert.False(gate.TryAccept(42, "Alice", 102));
    }

    [Theory]
    [InlineData(1, "PT123", 42)]
    [InlineData(3, "PT123", 42)]
    [InlineData(2, "pt123", 42)]
    [InlineData(2, "hello PT123", 42)]
    [InlineData(2, "", 42)]
    [InlineData(2, "PT123", 1)]
    public void OtherChannelsWrongMessagesAndSelfMessagesDoNotAuthorize(byte channel, string message, int sender)
    {
        var gate = Enabled();
        Assert.False(gate.Receive(channel, sender, "Alice", message, 1, 100));
        Assert.False(gate.TryAccept(sender, "Alice", 101));
    }

    [Fact]
    public void MatchingMessageCanArriveAfterTheInvitation()
    {
        var gate = Enabled();
        Assert.False(gate.TryAccept(42, "Alice", 100));
        Assert.True(gate.Receive(2, 42, "Alice", "PT123", 1, 101));
        Assert.True(gate.TryAccept(42, "Alice", 102));
    }

    [Fact]
    public void AuthorizationExpiresAtTwoMinutes()
    {
        var gate = Enabled();
        gate.Receive(2, 42, "Alice", "PT123", 1, 100);
        Assert.False(gate.TryAccept(42, "Alice", 220));
    }

    [Fact]
    public void CodeChangeAndDisableDiscardExistingAuthorizations()
    {
        var gate = Enabled();
        gate.Receive(2, 42, "Alice", "PT123", 1, 100);
        gate.Configure(true, "NEW");
        Assert.False(gate.TryAccept(42, "Alice", 101));
        gate.Receive(2, 42, "Alice", "NEW", 1, 102);
        gate.Configure(false, "NEW");
        gate.Configure(true, "NEW");
        Assert.False(gate.TryAccept(42, "Alice", 103));
    }

    [Fact]
    public void EmptyCodeNeverMatchesAndStoppingClearsMatches()
    {
        var gate = Enabled();
        gate.Configure(true, " ");
        Assert.False(gate.Receive(2, 42, "Alice", " ", 1, 100));
        gate.Configure(true, "PT123");
        gate.Receive(2, 42, "Alice", "PT123", 1, 101);
        gate.Clear();
        Assert.False(gate.TryAccept(42, "Alice", 102));
    }
}
