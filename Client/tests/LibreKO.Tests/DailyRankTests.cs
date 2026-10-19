using System;
using System.Linq;
using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class DailyRankTests
{
    private static Packet Reply(Action<Packet> body)
    {
        var p = new Packet(GameOpcodes.GS_DAILY_RANK);
        body(p);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void ABoardReadsPlaceChangeAndNamesNumberedInOrder()
    {
        var data = DailyRankWire.ReadBoard(Reply(w =>
        {
            w.WriteByte(0);
            w.WriteInt(4);
            w.WriteInt(-2);
            w.WriteShort(2);
            w.WriteSByteString("Alpha");
            w.WriteSByteString("Bravo");
        }))!;

        Assert.Equal((0, 4, -2), (data.Board, data.MyPlace, data.Change));
        Assert.Equal(new[] { new DailyRankEntry(1, "Alpha"), new DailyRankEntry(2, "Bravo") }, data.Entries);
    }

    [Fact]
    public void TheDrakiBoardHasNoChangeField()
    {
        var data = DailyRankWire.ReadBoard(Reply(w =>
        {
            w.WriteByte(DailyRank.DrakiBoard);
            w.WriteInt(1);
            w.WriteShort(1);
            w.WriteSByteString("Alpha");
        }))!;

        Assert.Equal(0, data.Change);
        Assert.Equal("Alpha", data.Entries.Single().Name);
    }

    [Fact]
    public void AnUnrankedKnightOfAdonisShowsNoPlace()
    {
        var data = DailyRankWire.ReadBoard(Reply(w =>
        {
            w.WriteByte(DailyRank.AdonisBoard);
            w.WriteInt(DailyRank.AdonisUnranked);
            w.WriteInt(0);
            w.WriteShort(0);
        }))!;

        Assert.Equal(0, data.MyPlace);
        Assert.Equal("-", DailyRank.PlaceShown(data.MyPlace));
    }

    [Fact]
    public void AnEmptyNameStillUsesUpItsNumber()
    {
        var data = DailyRankWire.ReadBoard(Reply(w =>
        {
            w.WriteByte(0);
            w.WriteInt(0);
            w.WriteInt(0);
            w.WriteShort(3);
            w.WriteSByteString("Alpha");
            w.WriteSByteString("");
            w.WriteSByteString("Charlie");
        }))!;

        Assert.Equal(new[] { 1, 3 }, data.Entries.Select(e => e.Rank));
    }

    [Fact]
    public void NamesPastTheHundredthAreNotRead()
    {
        var data = DailyRankWire.ReadBoard(Reply(w =>
        {
            w.WriteByte(0);
            w.WriteInt(0);
            w.WriteInt(0);
            w.WriteShort(DailyRank.MaxNames + 2);
            for (int i = 0; i < DailyRank.MaxNames + 2; i++) w.WriteSByteString($"N{i}");
        }))!;

        Assert.Equal(DailyRank.MaxNames, data.Entries.Count);
    }

    [Fact]
    public void ABoardPastTheLastSlotIsDropped()
    {
        Assert.Null(DailyRankWire.ReadBoard(Reply(w =>
        {
            w.WriteByte(DailyRank.BoardSlots);
            w.WriteInt(0);
            w.WriteInt(0);
            w.WriteShort(0);
        })));
    }

    [Theory]
    [InlineData(0, DailyRank.NewEntryChange, "New", DailyRankChangeIcon.Same)]
    [InlineData(0, 0, "-", DailyRankChangeIcon.Same)]
    [InlineData(0, 3, "+3", DailyRankChangeIcon.Up)]
    [InlineData(0, -2, "-2", DailyRankChangeIcon.Down)]
    [InlineData(DailyRank.DrakiBoard, 3, "", DailyRankChangeIcon.None)]
    public void TheChangeShowsLike2619(int board, int change, string text, DailyRankChangeIcon icon)
    {
        Assert.Equal((text, icon), DailyRank.ChangeShown(board, change));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 1)]
    [InlineData(9, 2)]
    [InlineData(16, 2)]
    [InlineData(100, 13)]
    public void PagesHoldEightRows(int entries, int pages)
    {
        Assert.Equal(pages, DailyRank.PageCount(entries));
    }

    [Fact]
    public void TheIconBlinksUntilOpenedToday()
    {
        var today = new DateTime(2026, 10, 9, 8, 0, 0);

        Assert.Equal(1009, DailyRank.DayKey(today));
        Assert.True(DailyRank.ShouldBlink(0, today));
        Assert.True(DailyRank.ShouldBlink(1008, today));
        Assert.False(DailyRank.ShouldBlink(1009, today));
    }
}
