namespace LibreKO.Session;

public sealed class WarehouseSession
{
    public event Action? WarehouseNpcEvent;

    internal void RaiseWarehouseNpc() => WarehouseNpcEvent?.Invoke();

    public event Action<int, ItemSlot[]>? WarehouseContentsEvent;

    internal void RaiseWarehouseContents(int arg1, ItemSlot[] itemSlot) => WarehouseContentsEvent?.Invoke(arg1, itemSlot);

    public event Action<byte, byte>? WarehouseResultEvent;

    internal void RaiseWarehouseResult(byte arg1, byte arg2) => WarehouseResultEvent?.Invoke(arg1, arg2);

    public event Action<WarehouseOpenResult>? WarehouseOpenRefusedEvent;

    internal void RaiseWarehouseOpenRefused(WarehouseOpenResult value) => WarehouseOpenRefusedEvent?.Invoke(value);
}
