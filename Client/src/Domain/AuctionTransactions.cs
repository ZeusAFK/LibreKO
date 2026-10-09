namespace LibreKO.Domain;

public sealed class AuctionTransactions
{
    public sealed record BidRequest(AuctionLot Lot, long Total, int Group, int Day)
    {
        public bool IsFor(AuctionToday today) => today.Group == Group && today.Day == Day;

        public bool Raises(AuctionLot lot) => lot.Slot == Lot.Slot && lot.ItemId == Lot.ItemId && lot.Current <= Total;

        public IReadOnlyList<AuctionLot> Apply(IEnumerable<AuctionLot> lots, string bidder) =>
            lots.Select(l => Raises(l) ? l with { Current = Total, TopBidder = bidder } : l).ToList();
    }

    public sealed record RowRequest(AuctionBidRow Row, bool Claim);

    public BidRequest? Bid { get; private set; }
    public RowRequest? Row { get; private set; }
    public bool Busy => Bid != null || Row != null;

    public bool BeginBid(AuctionLot lot, long total, int group, int day)
    {
        if (Busy) return false;
        Bid = new(lot, total, group, day);
        return true;
    }

    public bool BeginRow(AuctionBidRow row, bool claim)
    {
        if (Busy) return false;
        Row = new(row, claim);
        return true;
    }

    public BidRequest? CompleteBid()
    {
        var request = Bid;
        Bid = null;
        return request;
    }

    public RowRequest? CompleteRow(bool claim)
    {
        if (Row == null || Row.Claim != claim) return null;
        var request = Row;
        Row = null;
        return request;
    }
}
