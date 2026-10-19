namespace LibreKO.Session;

public sealed class RentalSession
{
    public event Action? RentalUnavailableEvent;

    internal void RaiseRentalUnavailable() => RentalUnavailableEvent?.Invoke();
}
