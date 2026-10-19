namespace LibreKO.Session;

public sealed class ZoneConcurrentSession
{
    public event Action<IReadOnlyList<ZoneConcurrentCount>>? ZoneConcurrentEvent;

    internal void RaiseZoneConcurrent(IReadOnlyList<ZoneConcurrentCount> iReadOnlyList) => ZoneConcurrentEvent?.Invoke(iReadOnlyList);
}
