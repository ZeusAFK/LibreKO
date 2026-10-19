namespace LibreKO.Session;

public sealed class ClanWarehouseSession
{
    public event Action<int, ItemSlot[]>? ClanWhContentsEvent;

    internal void RaiseClanWhContents(int arg1, ItemSlot[] itemSlot) => ClanWhContentsEvent?.Invoke(arg1, itemSlot);

    public event Action<WarehouseSubOpcode, ClanWarehouseResult>? ClanWhResultEvent;

    internal void RaiseClanWhResult(WarehouseSubOpcode arg1, ClanWarehouseResult arg2) => ClanWhResultEvent?.Invoke(arg1, arg2);
}
