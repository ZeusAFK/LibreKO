namespace LibreKO.Session;

public sealed class PvpSession
{
    public event Action<int, string, string>? PvpRivalAssignedEvent;

    internal void RaisePvpRivalAssigned(int arg1, string arg2, string arg3) => PvpRivalAssignedEvent?.Invoke(arg1, arg2, arg3);

    public event Action? PvpRivalRemovedEvent;

    internal void RaisePvpRivalRemoved() => PvpRivalRemovedEvent?.Invoke();

    public event Action<int, bool>? PvpAngerEvent;

    private void RaisePvpAnger(int arg1, bool arg2) => PvpAngerEvent?.Invoke(arg1, arg2);

    public int AngerGauge { get; private set; }

    public bool AngerFull { get; private set; }

    internal void ResetAnger()
    {
        AngerGauge = 0;
        AngerFull = false;
    }

    internal void ApplyAnger(int gauge, bool full)
    {
        AngerGauge = gauge;
        AngerFull = full;
        RaisePvpAnger(gauge, full);
    }
}
