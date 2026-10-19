namespace LibreKO.Session;

public sealed class MerchantSearchSession
{
    public event Action? MerchantSearchOpenEvent;

    internal void RaiseMerchantSearchOpen() => MerchantSearchOpenEvent?.Invoke();

    public event Action<IReadOnlyList<MerchantSearchRow>>? MerchantSearchRowsEvent;

    internal void RaiseMerchantSearchRows(IReadOnlyList<MerchantSearchRow> iReadOnlyList) => MerchantSearchRowsEvent?.Invoke(iReadOnlyList);

    public event Action? MerchantSearchLoadedEvent;

    internal void RaiseMerchantSearchLoaded() => MerchantSearchLoadedEvent?.Invoke();

    public event Action<int>? MerchantSearchRefusedEvent;

    internal void RaiseMerchantSearchRefused(int value) => MerchantSearchRefusedEvent?.Invoke(value);

    public event Action? MerchantSearchMovedEvent;

    internal void RaiseMerchantSearchMoved() => MerchantSearchMovedEvent?.Invoke();
}
