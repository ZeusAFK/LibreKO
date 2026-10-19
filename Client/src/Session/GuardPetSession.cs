namespace LibreKO.Session;

public sealed class GuardPetSession
{
    public event Action<GuardPetStatus>? GuardPetStatusEvent;

    internal void RaiseGuardPetStatus(GuardPetStatus guardPetStatus) => GuardPetStatusEvent?.Invoke(guardPetStatus);
}
