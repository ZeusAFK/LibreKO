namespace LibreKO.Session;

public sealed class SpecialAuctionSession
{
    public event Action<AuctionToday>? AuctionTodayEvent;

    internal void RaiseAuctionToday(AuctionToday auctionToday) => AuctionTodayEvent?.Invoke(auctionToday);

    public event Action<short>? AuctionBidEvent;

    internal void RaiseAuctionBid(short value) => AuctionBidEvent?.Invoke(value);

    public event Action<short>? AuctionCollectEvent;

    internal void RaiseAuctionCollect(short value) => AuctionCollectEvent?.Invoke(value);

    public event Action<short, IReadOnlyList<AuctionBidRow>>? AuctionMyInfoEvent;

    internal void RaiseAuctionMyInfo(short arg1, IReadOnlyList<AuctionBidRow> iReadOnlyList) => AuctionMyInfoEvent?.Invoke(arg1, iReadOnlyList);

    public event Action<IReadOnlyList<AuctionResultLine>>? AuctionEndedEvent;

    internal void RaiseAuctionEnded(IReadOnlyList<AuctionResultLine> iReadOnlyList) => AuctionEndedEvent?.Invoke(iReadOnlyList);

    public event Action<bool>? AuctionMiscarriedEvent;

    internal void RaiseAuctionMiscarried(bool value) => AuctionMiscarriedEvent?.Invoke(value);

    public event Action<short>? AuctionClaimEvent;

    internal void RaiseAuctionClaim(short value) => AuctionClaimEvent?.Invoke(value);

    public event Action<IReadOnlyList<IReadOnlyList<AuctionResultLine>>>? AuctionLogEvent;

    internal void RaiseAuctionLog(IReadOnlyList<IReadOnlyList<AuctionResultLine>> iReadOnlyList) => AuctionLogEvent?.Invoke(iReadOnlyList);
}
