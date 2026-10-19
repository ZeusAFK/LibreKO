namespace LibreKO.Session;

public sealed class VendorSession
{
    public bool TradeInFlight { get; private set; }

    internal void BeginTrade() => TradeInFlight = true;

    internal void EndTrade() => TradeInFlight = false;

    public event Action<int>? TradeNpcEvent;

    internal void RaiseTradeNpc(int value) => TradeNpcEvent?.Invoke(value);

    public event Action<bool, int, int, int>? ItemTradeResultEvent;

    internal void RaiseItemTradeResult(bool arg1, int arg2, int arg3, int arg4) => ItemTradeResultEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action? ItemTradeMovedEvent;

    internal void RaiseItemTradeMoved() => ItemTradeMovedEvent?.Invoke();

    public event Action<RepurchaseReply>? RepurchaseEvent;

    internal void RaiseRepurchase(RepurchaseReply repurchaseReply) => RepurchaseEvent?.Invoke(repurchaseReply);
}
