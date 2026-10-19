namespace LibreKO.Session;

public sealed class MerchantSession
{
    private readonly Dictionary<int, StallOwner> knownStalls = new();

    public IEnumerable<StallOwner> KnownStalls => knownStalls.Values;

    internal void RememberStall(StallOwner owner) => knownStalls[owner.CharacterId] = owner;

    internal void ForgetStall(int characterId) => knownStalls.Remove(characterId);

    internal void ForgetStalls() => knownStalls.Clear();

    public event Action<MerchantOpenResult>? MerchantOpenResultEvent;

    internal void RaiseMerchantOpenResult(MerchantOpenResult value) => MerchantOpenResultEvent?.Invoke(value);

    public event Action<MerchantItemAddReply>? MerchantItemAddEvent;

    internal void RaiseMerchantItemAdd(MerchantItemAddReply reply) => MerchantItemAddEvent?.Invoke(reply);

    public event Action<bool, int>? MerchantItemCancelEvent;

    internal void RaiseMerchantItemCancel(bool arg1, int arg2) => MerchantItemCancelEvent?.Invoke(arg1, arg2);

    public event Action<int, MerchantStallItem[]>? MerchantListEvent;

    internal void RaiseMerchantList(int arg1, MerchantStallItem[] merchantStallItem) => MerchantListEvent?.Invoke(arg1, merchantStallItem);

    public event Action<MerchantBuyReply>? MerchantBuyEvent;

    internal void RaiseMerchantBuy(MerchantBuyReply reply) => MerchantBuyEvent?.Invoke(reply);

    public event Action<int, string>? MerchantSoldEvent;

    internal void RaiseMerchantSold(int arg1, string arg2) => MerchantSoldEvent?.Invoke(arg1, arg2);

    public event Action<MerchantStallOpened>? MerchantInsertedEvent;

    internal void RaiseMerchantInserted(MerchantStallOpened stall) => MerchantInsertedEvent?.Invoke(stall);

    public event Action<int>? MerchantStallClosedEvent;

    internal void RaiseMerchantStallClosed(int value) => MerchantStallClosedEvent?.Invoke(value);

    public event Action<BuyingMerchantResult>? BuyMerchantOpenEvent;

    internal void RaiseBuyMerchantOpen(BuyingMerchantResult value) => BuyMerchantOpenEvent?.Invoke(value);

    public event Action<BuyingMerchantResult>? BuyMerchantInsertEvent;

    internal void RaiseBuyMerchantInsert(BuyingMerchantResult value) => BuyMerchantInsertEvent?.Invoke(value);

    public event Action<int, MerchantStallItem[]>? BuyMerchantListEvent;

    internal void RaiseBuyMerchantList(int arg1, MerchantStallItem[] merchantStallItem) => BuyMerchantListEvent?.Invoke(arg1, merchantStallItem);

    public event Action<BuyingMerchantResult>? BuyMerchantResultEvent;

    internal void RaiseBuyMerchantResult(BuyingMerchantResult value) => BuyMerchantResultEvent?.Invoke(value);

    public event Action<int, int, int, int>? BuyMerchantSoldEvent;

    internal void RaiseBuyMerchantSold(int arg1, int arg2, int arg3, int arg4) => BuyMerchantSoldEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action<int, int, string>? BuyMerchantBoughtEvent;

    internal void RaiseBuyMerchantBought(int arg1, int arg2, string arg3) => BuyMerchantBoughtEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int>? BuyMerchantClosedEvent;

    internal void RaiseBuyMerchantClosed(int value) => BuyMerchantClosedEvent?.Invoke(value);

    public event Action<int, int[]>? BuyStallPlacedEvent;

    internal void RaiseBuyStallPlaced(int arg1, int[] arg2) => BuyStallPlacedEvent?.Invoke(arg1, arg2);

    public event Action<StallOwner, int[]>? StallListEvent;

    internal void RaiseStallList(StallOwner stallOwner, int[] arg2) => StallListEvent?.Invoke(stallOwner, arg2);

    public event Action<List<StallOwner>>? StallsInViewEvent;

    internal void RaiseStallsInView(List<StallOwner> list) => StallsInViewEvent?.Invoke(list);

    public event Action<StallShowcaseReply>? StallShowcaseChangedEvent;

    internal void RaiseStallShowcaseChanged(StallShowcaseReply stallShowcaseReply) => StallShowcaseChangedEvent?.Invoke(stallShowcaseReply);
}
