namespace LibreKO.Session;

public sealed class RepairSession
{
    public event Action<int>? RepairNpcEvent;

    internal void RaiseRepairNpc(int value) => RepairNpcEvent?.Invoke(value);

    public event Action<bool, int>? ItemRepairResultEvent;

    internal void RaiseItemRepairResult(bool arg1, int arg2) => ItemRepairResultEvent?.Invoke(arg1, arg2);
}
