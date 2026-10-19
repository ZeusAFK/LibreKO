namespace LibreKO.Session;

public sealed class GlobalMapSession
{
    public event Action<List<GlobalMapZone>>? GlobalMapListEvent;

    internal void RaiseGlobalMapList(List<GlobalMapZone> list) => GlobalMapListEvent?.Invoke(list);
}
