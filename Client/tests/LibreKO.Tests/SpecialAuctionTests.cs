using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class SpecialAuctionTests
{
    private const int Earrings = 1310610106;
    private const int Ring = 1399312170;
    private const int ShadowPiece = 700009000;
    private const long Million = 1_000_000;

    private const string Table = """
        [
         {"row": 1, "slot": 0, "item": 1310610106, "count": 1, "start": 1000000, "step": 1000000, "secret": false},
         {"row": 1, "slot": 2, "item": 1399312170, "count": 1, "start": 1000000, "step": 1000000, "secret": false},
         {"row": 2, "slot": 0, "item": 700009000, "count": 1, "start": 200000000, "step": 10000000, "secret": false},
         {"row": 13, "slot": 0, "item": 811059000, "count": 1, "start": 1500000000, "step": 10000000, "secret": true},
         {"row": 15, "slot": 0, "item": 1310610106, "count": 1, "start": 1000000, "step": 1000000, "secret": false}
        ]
        """;

    private static AuctionScheduleLot[] All => SpecialAuction.Parse(Table);

    [Fact]
    public void EveryLotIsRead()
    {
        var lot = All.Single(l => l.Row == 13);
        Assert.Equal(5, All.Length);
        Assert.Equal(1_500_000_000, lot.Start);
        Assert.Equal(10_000_000, lot.Step);
        Assert.True(lot.Secret);
    }

    [Fact]
    public void TheGroupAndDayPickTheTableRow()
    {
        Assert.Equal(1, SpecialAuction.Row(group: 1, day: 1));
        Assert.Equal(15, SpecialAuction.Row(group: 2, day: 1));
        Assert.Equal([Earrings, Ring], SpecialAuction.LotsOf(All, 1, 1).Select(l => l.Item));
    }

    [Fact]
    public void AnOfferWithoutBidsStartsAtTheTablePrice()
    {
        var today = new AuctionToday(SpecialAuction.Bidding, 1, 3600, 1,
            [new AuctionOffer(0, Earrings, 0, ""), new AuctionOffer(2, Ring, 5 * Million, "Rikka")]);

        var lots = SpecialAuction.Live(All, today);

        Assert.Equal(Million, lots[0].Current);
        Assert.Equal(2 * Million, lots[0].MinimumBid);
        Assert.Equal(5 * Million, lots[1].Current);
        Assert.Equal("Rikka", lots[1].TopBidder);
    }

    [Fact]
    public void AnOfferThatDoesNotMatchTheTableIsDropped()
    {
        var today = new AuctionToday(SpecialAuction.Bidding, 1, 3600, 1,
            [new AuctionOffer(0, Ring, 0, ""), new AuctionOffer(5, Earrings, 0, "")]);

        Assert.Empty(SpecialAuction.Live(All, today));
    }

    [Fact]
    public void TheScheduleShowsTheRestOfTheGroup()
    {
        var days = SpecialAuction.Upcoming(All, group: 1, day: 1);

        Assert.Equal(12, days.Count);
        Assert.Equal(1, days[0].Offset);
        Assert.Equal(ShadowPiece, days[0].Lots.Single().Item);
        Assert.Equal(12, days[^1].Offset);
        Assert.Empty(SpecialAuction.Upcoming(All, group: 1, day: SpecialAuction.DaysPerGroup));
    }

    [Theory]
    [InlineData(5, 10_000_000, 5)]
    [InlineData(50, 10_000_000, 10)]
    [InlineData(2000, 5_000_000_000, 999)]
    [InlineData(3, 999_999, 0)]
    [InlineData(-4, 10_000_000, 0)]
    public void TheCoinEntryIsMillionsWithinTheGold(long typed, long gold, int expected) =>
        Assert.Equal(expected, SpecialAuction.ClampMillions(typed, gold));

    [Fact]
    public void ChecksCountOneBillionEach()
    {
        Assert.Equal(2, SpecialAuction.ClampChecks(5, checksInBag: 2));
        Assert.Equal(2_003_000_000, SpecialAuction.Total(millions: 3, checks: 2));
    }

    [Fact]
    public void ABidMustReachTheAvailableBid()
    {
        var lot = new AuctionLot(0, Earrings, 1, 5 * Million, (int)Million, "");

        Assert.Equal(SpecialAuction.NoLotText, SpecialAuction.BidRefusal(null, 5, 0, 0));
        Assert.Equal(SpecialAuction.NotEnoughBalanceText, SpecialAuction.BidRefusal(lot, 0, 0, 0));
        Assert.Equal(SpecialAuction.NotEnoughBalanceText, SpecialAuction.BidRefusal(lot, 5, 0, 0));
        Assert.Equal(SpecialAuction.WrongChecksText, SpecialAuction.BidRefusal(lot, 0, 2, 1));
        Assert.Equal(0, SpecialAuction.BidRefusal(lot, 6, 0, 0));
    }

    [Fact]
    public void ResultsShowTheClientsOwnTexts()
    {
        Assert.Equal(43686, SpecialAuction.BidResultText(1));
        Assert.Equal(43703, SpecialAuction.BidResultText(-9));
        Assert.Equal(SpecialAuction.SystemErrorText, SpecialAuction.BidResultText(-10));
        Assert.True(SpecialAuction.Collected(2));
        Assert.Equal(43716, SpecialAuction.CollectResultText(-9));
        Assert.Equal(10714, SpecialAuction.ClaimResultText(-4));
    }

    [Fact]
    public void OnlyAnOutbidOrCancelledBidCanBeRetracted()
    {
        Assert.False(SpecialAuction.CanRetract(SpecialAuction.TopBidder));
        Assert.True(SpecialAuction.CanRetract(SpecialAuction.Outbid));
        Assert.True(SpecialAuction.CanRetract(SpecialAuction.Cancelled));
        Assert.False(SpecialAuction.IsBid(SpecialAuction.Won));
    }

    [Fact]
    public void TimeAndAmountsReadPlainly()
    {
        Assert.Equal("02:55:09", SpecialAuction.Clock(2 * 3600 + 55 * 60 + 9));
        Assert.Equal("00:00:00", SpecialAuction.Clock(-3));
        Assert.Equal("2 billion 100 million", SpecialAuction.InWords(2_100_000_000));
        Assert.Equal("", SpecialAuction.InWords(0));
    }

    [Fact]
    public void TheLogColoursFollowTheDay()
    {
        Assert.Equal("f0f8ff", SpecialAuction.LogColour(0));
        Assert.Equal("ffffff", SpecialAuction.LogColour(11));
        Assert.Equal("ffffff", SpecialAuction.LogColour(20));
    }

    [Fact]
    public void ChecksAreFoundInBagOrder()
    {
        var inventory = new Inventory();
        inventory.EnsureLength(Inventory.GridStart + Inventory.GridCount);
        inventory.ApplySlotUpdate(Inventory.GridStart + 4, new ItemSlot { ItemId = SpecialAuction.MythrilCheck, Count = 1 });
        inventory.ApplySlotUpdate(Inventory.GridStart + 1, new ItemSlot { ItemId = SpecialAuction.MythrilCheck, Count = 1 });

        Assert.Equal([Inventory.GridStart + 1, Inventory.GridStart + 4], SpecialAuction.CheckSlots(inventory));
    }
}
