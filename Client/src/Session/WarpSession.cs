namespace LibreKO.Session;

public sealed class WarpSession
{
    public event Action<List<WarpListEntry>>? WarpListEvent;

    internal void RaiseWarpList(List<WarpListEntry> list) => WarpListEvent?.Invoke(list);

    public event Action<WarpResult, int>? WarpFailEvent;

    internal void RaiseWarpFail(WarpResult arg1, int arg2) => WarpFailEvent?.Invoke(arg1, arg2);
}
