namespace LibreKO.Session;

public sealed class ExchangeSession
{
    public event Action<int>? ExchangeRequestEvent;

    internal void RaiseExchangeRequest(int value) => ExchangeRequestEvent?.Invoke(value);

    public event Action<bool>? ExchangeAgreeEvent;

    internal void RaiseExchangeAgree(bool value) => ExchangeAgreeEvent?.Invoke(value);

    public event Action<bool>? ExchangeAddResultEvent;

    internal void RaiseExchangeAddResult(bool value) => ExchangeAddResultEvent?.Invoke(value);

    public event Action<int, int, short, int>? ExchangeOtherAddEvent;

    internal void RaiseExchangeOtherAdd(int arg1, int arg2, short arg3, int arg4) => ExchangeOtherAddEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action? ExchangeOtherDecideEvent;

    internal void RaiseExchangeOtherDecide() => ExchangeOtherDecideEvent?.Invoke();

    public event Action<bool, int, List<(byte DstPos, ItemSlot Slot)>>? ExchangeDoneEvent;

    internal void RaiseExchangeDone(bool arg1, int arg2, List<(byte DstPos, ItemSlot Slot)> list) => ExchangeDoneEvent?.Invoke(arg1, arg2, list);

    public event Action? ExchangeCancelEvent;

    internal void RaiseExchangeCancel() => ExchangeCancelEvent?.Invoke();

    public event Action? ExchangeDuplicateEvent;

    internal void RaiseExchangeDuplicate() => ExchangeDuplicateEvent?.Invoke();
}
