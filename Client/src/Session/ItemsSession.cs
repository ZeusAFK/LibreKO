namespace LibreKO.Session;

public sealed class ItemsSession
{
    public const int NoPendingSlot = -1;

    public PendingItemMove? PendingMove { get; internal set; }

    public int PendingRemoveSlot { get; internal set; } = NoPendingSlot;

    public event Action<bool>? ItemMoveResultEvent;

    internal void RaiseItemMoveResult(bool value) => ItemMoveResultEvent?.Invoke(value);

    public event Action<DerivedStats>? ItemStatsEvent;

    internal void RaiseItemStats(DerivedStats derivedStats) => ItemStatsEvent?.Invoke(derivedStats);

    public event Action<int, ItemSlot>? InventorySlotEvent;

    internal void RaiseInventorySlot(int arg1, ItemSlot itemSlot) => InventorySlotEvent?.Invoke(arg1, itemSlot);

    public event Action<int, int>? ItemGainedEvent;

    internal void RaiseItemGained(int arg1, int arg2) => ItemGainedEvent?.Invoke(arg1, arg2);

    public event Action<ItemSlot[]>? InventoryGridRefreshEvent;

    internal void RaiseInventoryGridRefresh(ItemSlot[] itemSlot) => InventoryGridRefreshEvent?.Invoke(itemSlot);

    public event Action<bool, ItemRemoveReason>? ItemRemoveResultEvent;

    internal void RaiseItemRemoveResult(bool ok, ItemRemoveReason reason) => ItemRemoveResultEvent?.Invoke(ok, reason);

    public event Action<int, int, int, short>? LookChangeEvent;

    internal void RaiseLookChange(int arg1, int arg2, int arg3, short arg4) => LookChangeEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action? InventoryArrangeFailedEvent;

    internal void RaiseInventoryArrangeFailed() => InventoryArrangeFailedEvent?.Invoke();

    public event Action<int, bool>? ItemReceivedEvent;

    internal void RaiseItemReceived(int arg1, bool arg2) => ItemReceivedEvent?.Invoke(arg1, arg2);

    public bool TimedItemsOffered { get; private set; }

    internal void NoteTimedItemsOffered() => TimedItemsOffered = true;

    internal void ForgetTimedItemsOffer() => TimedItemsOffered = false;

    internal void ApplyRemoveResult(bool ok, ItemRemoveReason reason)
    {
        PendingRemoveSlot = NoPendingSlot;
        RaiseItemRemoveResult(ok, reason);
    }

    internal void CancelPendingRequests()
    {
        bool moving = PendingMove.HasValue;
        PendingMove = null;
        PendingRemoveSlot = NoPendingSlot;
        if (moving) RaiseItemMoveResult(false);
    }
}
