using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class EventPostTests
{
    private static Packet Reply(EventBoardSubOpcode sub, uint eventId, uint result)
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)sub);
        p.WriteUInt(eventId);
        p.WriteUInt(result);
        return p;
    }

    [Fact]
    public void AnAcceptedPostCarriesTheCountersThePeriodAndTheRecentPosts()
    {
        var p = Reply(EventBoardSubOpcode.PostRegister, 0, 1);
        p.WriteUInt(4);
        p.WriteUInt(120);
        p.WriteUInt(1_793_487_600);
        p.WriteUInt(1_796_079_599);
        p.WriteShort(2);
        foreach (var (name, time) in new[] { ("Rin", 1_793_700_000u), ("Zeus", 1_793_600_000u) })
        {
            p.WriteSByteString("Destan");
            p.WriteSByteString(name);
            p.WriteSByteString("I Love Knight Online");
            p.WriteUInt(time);
        }
        p.ResetOffset();

        Assert.Equal(EventBoardSubOpcode.PostRegister, (EventBoardSubOpcode)p.ReadByte());
        var reply = EventPostWire.ReadReply(p)!;

        Assert.Equal(EventPostEvent.LoveKnightOnline, reply.Event);
        Assert.Equal(EventPostResult.Accepted, reply.Result);
        var board = reply.Board!;
        Assert.Equal((4u, 120u, 1_793_487_600u, 1_796_079_599u), (board.Today, board.InPeriod, board.PeriodStart, board.PeriodEnd));
        Assert.Equal(new EventPostEntry("Destan", "Rin", "I Love Knight Online", 1_793_700_000), board.Posts[0]);
        Assert.Equal("Zeus", board.Posts[1].Name);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ARefusedPostCarriesOnlyItsResult()
    {
        var p = Reply(EventBoardSubOpcode.PostRegister, 1, 102);
        p.ResetOffset();
        p.ReadByte();

        Assert.Equal(new EventPostReply(EventPostEvent.ContinuePlaying, EventPostResult.AlreadyPosted, null), EventPostWire.ReadReply(p));
    }

    [Fact]
    public void TheHistoryListsEachEventAndTime()
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)EventBoardSubOpcode.PostHistory);
        p.WriteUInt(1);
        p.WriteShort(2);
        p.WriteByte(1);
        p.WriteUInt(1_793_700_000);
        p.WriteByte(0);
        p.WriteUInt(1_793_600_000);
        p.ResetOffset();
        p.ReadByte();

        Assert.Equal(
            new[]
            {
                new EventPostHistoryEntry(EventPostEvent.ContinuePlaying, 1_793_700_000),
                new EventPostHistoryEntry(EventPostEvent.LoveKnightOnline, 1_793_600_000),
            },
            EventPostWire.ReadHistory(p));
    }

    [Theory]
    [InlineData(EventPostResult.WrongText, 33218)]
    [InlineData(EventPostResult.AlreadyPosted, 33219)]
    [InlineData(EventPostResult.TryLater, 16810)]
    [InlineData(EventPostResult.Failed, 33214)]
    public void EachRefusalShowsItsRetailText(EventPostResult result, int text)
    {
        Assert.Equal(text, EventPostText.Refusal(result)!.Value.Text);
    }

    [Fact]
    public void AnAcceptedPostShowsNoRefusal()
    {
        Assert.Null(EventPostText.Refusal(EventPostResult.Accepted));
    }

    [Theory]
    [InlineData(EventPostEvent.LoveKnightOnline, 33213, "I Love Knight Online")]
    [InlineData(EventPostEvent.ContinuePlaying, 33223, "I Will Continue Playing Knight Online")]
    public void EachEventNamesItsPhrase(EventPostEvent eventId, int title, string phrase)
    {
        Assert.Equal((title, phrase), EventPostText.Title(eventId));
        Assert.Equal(phrase, EventPostText.Phrase(eventId));
    }

    [Theory]
    [InlineData(0, 3, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 3, 2)]
    [InlineData(100, 10, 10)]
    public void ThePageCountRoundsUpAndIsNeverZero(int entries, int perPage, int pages)
    {
        Assert.Equal(pages, Paging.Count(entries, perPage));
    }
}
