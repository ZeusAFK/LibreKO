namespace LibreKO.Session;

public sealed class GenieSession
{
    public bool GenieRunning { get; private set; }

    public void Reset() => GenieRunning = false;

    public event Action<string, bool>? GenieStatusEvent;

    internal void RaiseGenieStatus(string arg1, bool arg2) => GenieStatusEvent?.Invoke(arg1, arg2);

    public event Action<bool, int>? GenieClaimEvent;

    internal void RaiseGenieClaim(bool arg1, int arg2) => GenieClaimEvent?.Invoke(arg1, arg2);

    public event Action<bool>? GenieHammerResult;

    internal void RaiseGenieHammerResult(bool value) => GenieHammerResult?.Invoke(value);

    public event Action<int, bool>? GenieAuraEvent;

    internal void RaiseGenieAura(int arg1, bool arg2) => GenieAuraEvent?.Invoke(arg1, arg2);

    public event Action<bool, int>? GenieSystemState;

    internal void RaiseGenieSystemState(bool arg1, int arg2) => GenieSystemState?.Invoke(arg1, arg2);

    public event Action<byte[]>? GenieOptionsReceived;

    internal void RaiseGenieOptionsReceived(byte[] value) => GenieOptionsReceived?.Invoke(value);

    internal void ApplySystemState(bool running, int minutes)
    {
        GenieRunning = running;
        RaiseGenieSystemState(running, minutes);
    }

    internal void ApplyRemainingTime(int minutes)
    {
        if (minutes == 0) GenieRunning = false;
        RaiseGenieSystemState(GenieRunning, minutes);
    }
}
