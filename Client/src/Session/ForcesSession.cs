namespace LibreKO.Session;

public sealed class ForcesSession
{
    public event Action<ForcesStatus>? ForcesStatusEvent;

    internal void RaiseForcesStatus(ForcesStatus forcesStatus) => ForcesStatusEvent?.Invoke(forcesStatus);

    public event Action<bool>? ForcesJoinEvent;

    internal void RaiseForcesJoin(bool value) => ForcesJoinEvent?.Invoke(value);

    public event Action<bool>? ForcesLeaveEvent;

    internal void RaiseForcesLeave(bool value) => ForcesLeaveEvent?.Invoke(value);
}
