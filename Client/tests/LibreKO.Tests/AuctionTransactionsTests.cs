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

    [Fact]
    public void AnAcceptedBidRaisesOnlyTheSubmittedLot()
    {
        const int OtherItem = 1310610107;
        var submitted = new AuctionTransactions.BidRequest(Lot(1), 3 * Million, Group: 2, Day: 7);
        AuctionLot[] lots = [Lot(1), Lot(2), Lot(1) with { ItemId = OtherItem }];

        var applied = submitted.Apply(lots, "Zeus");

        Assert.Equal(lots[0] with { Current = 3 * Million, TopBidder = "Zeus" }, applied[0]);
        Assert.Equal(lots[1], applied[1]);
        Assert.Equal(lots[2], applied[2]);
    }

    [Fact]
    public void ALotAlreadyAboveTheBidKeepsItsTopBidder()
    {
        var submitted = new AuctionTransactions.BidRequest(Lot(), 3 * Million, Group: 2, Day: 7);

        Assert.True(submitted.Raises(Lot() with { Current = 3 * Million }));
        Assert.False(submitted.Raises(Lot() with { Current = 3 * Million + 1 }));
        var outbid = Lot() with { Current = 4 * Million, TopBidder = "Zeus" };
        Assert.Equal(outbid, submitted.Apply([outbid], "Rikka")[0]);
    }

    [Theory]
    [InlineData(2, 7, true)]
    [InlineData(2, 8, false)]
    [InlineData(3, 7, false)]
    public void ABidReplyAppliesOnlyToTheAuctionDayItWasPlacedOn(int group, int day, bool applies)
    {
        var submitted = new AuctionTransactions.BidRequest(Lot(), 3 * Million, Group: 2, Day: 7);
        var today = new AuctionToday(SpecialAuction.Bidding, group, 0, day, []);

        Assert.Equal(applies, submitted.IsFor(today));
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
