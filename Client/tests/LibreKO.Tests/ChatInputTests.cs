using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ChatInputTests
{
    [Theory]
    [InlineData("hello", ChatType.Party, ChatType.Party)]
    [InlineData("!hello", ChatType.General, ChatType.Shout)]
    [InlineData("#hello", ChatType.General, ChatType.Party)]
    [InlineData("$hello", ChatType.General, ChatType.Clan)]
    [InlineData("&hello", ChatType.General, ChatType.Alliance)]
    [InlineData("%hello", ChatType.General, ChatType.Command)]
    [InlineData("@Rin hello", ChatType.General, ChatType.Private)]
    [InlineData("", ChatType.Shout, ChatType.Shout)]
    public void TheTypedPrefixPicksTheChannel(string text, byte selected, byte expected) =>
        Assert.Equal(expected, ChatPrefixes.ChannelFor(text, selected));

    [Fact]
    public void ACommandIsNotAChatChannel() =>
        Assert.Equal(ChatPrefixes.NotChat, ChatPrefixes.ChannelFor("/w Rin hi", ChatType.Party));

    [Fact]
    public void HistoryWalksBackAndReturnsTheDraft()
    {
        var history = new ChatInputHistory();
        history.Push("first");
        history.Push("second");

        Assert.Equal("second", history.Older("half typed"));
        Assert.Equal("first", history.Older("second"));
        Assert.Equal("first", history.Older("first"));
        Assert.Equal("second", history.Newer());
        Assert.Equal("half typed", history.Newer());
        Assert.Null(history.Newer());
    }

    [Fact]
    public void HistoryStoresARepeatOnceAndKeepsTheLastForty()
    {
        var history = new ChatInputHistory();
        history.Push("same");
        history.Push("same");
        Assert.Equal("same", history.Older(""));
        Assert.Equal("same", history.Older("same"));

        history.Reset();
        for (int i = 0; i < ChatInputHistory.Capacity + 5; i++) history.Push($"line {i}");
        string? oldest = null;
        for (int i = 0; i < ChatInputHistory.Capacity + 5; i++) oldest = history.Older(oldest ?? "");
        Assert.Equal("line 5", oldest);
    }

    [Fact]
    public void StoppingStartsTheNextWalkAtTheNewestLine()
    {
        var history = new ChatInputHistory();
        history.Push("first");
        history.Push("second");
        history.Older("");
        history.Older("second");
        history.StopBrowsing();
        Assert.Equal("second", history.Older("draft"));
        Assert.Equal("draft", history.Newer());
    }

    [Theory]
    [InlineData(103, null)]
    [InlineData(104, "104/128")]
    [InlineData(128, "128/128")]
    public void TheCounterShowsNearTheLimit(int length, string? expected) =>
        Assert.Equal(expected, ChatInputText.Counter(length, ChatInputText.MaxLength));

    [Fact]
    public void PastedLineBreaksBecomeOneSpace() =>
        Assert.Equal("a b c d", ChatInputText.Paste("a\r\n\r\nb\tc\u0007\nd"));
}
