namespace LibreKO.Session;

public sealed class GenderChangeSession
{
    public event Action<GenderChangeResult>? GenderChangeRefusedEvent;

    internal void RaiseGenderChangeRefused(GenderChangeResult value) => GenderChangeRefusedEvent?.Invoke(value);

    public event Action? GenderChangedEvent;

    internal void RaiseGenderChanged() => GenderChangedEvent?.Invoke();
}
