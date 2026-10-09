using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class AuctionTransactionsTests
{
    private const int Earrings = 1310610106;
    private const long Million = 1_000_000;

    private static AuctionLot Lot(int slot = 1) => new(slot, Earrings, 1, Million, (int)Million, "Rikka");

    private static AuctionBidRow Row(byte slot = 1) =>
        new(1, 2, slot, Earrings, 1, 42, 0, 2 * Million, SpecialAuction.Outbid);

    [Fact]
    public void TheBidReplyKeepsTheSubmittedLotAmountAndDay()
    {
        var pending = new AuctionTransactions();
        var lot = Lot();

        Assert.True(pending.BeginBid(lot, 1013 * Million, group: 2, day: 7));
        Assert.False(pending.BeginBid(Lot(3), 999 * Million, group: 3, day: 8));
        var submitted = pending.CompleteBid()!;

        Assert.Equal(lot, submitted.Lot);
        Assert.Equal(1013 * Million, submitted.Total);
        Assert.Equal(2, submitted.Group);
        Assert.Equal(7, submitted.Day);
        Assert.False(pending.Busy);
        Assert.Null(pending.CompleteBid());
    }

    [Fact]
    public void ABidAndARowRequestDoNotOverwriteEachOther()
    {
        var pending = new AuctionTransactions();

        Assert.True(pending.BeginBid(Lot(), 2 * Million, group: 1, day: 2));
        Assert.False(pending.BeginRow(Row(), claim: false));
        Assert.Null(pending.CompleteRow(claim: false));
        Assert.True(pending.Busy);

        pending.CompleteBid();
        Assert.True(pending.BeginRow(Row(), claim: false));
        Assert.False(pending.BeginBid(Lot(), 2 * Million, group: 1, day: 2));
        Assert.Null(pending.CompleteBid());
        Assert.True(pending.Busy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheWrongRowReplyLeavesThePendingRequest(bool claim)
    {
        var pending = new AuctionTransactions();
        var row = Row();

        Assert.True(pending.BeginRow(row, claim));
        Assert.False(pending.BeginRow(Row(4), claim));
        Assert.Null(pending.CompleteRow(!claim));
        Assert.True(pending.Busy);
        Assert.Equal(row, pending.CompleteRow(claim)!.Row);
        Assert.False(pending.Busy);
        Assert.Null(pending.CompleteRow(claim));
    }
}
