namespace LibreKO.Session;

public sealed class AwakenSession
{
    public event Action<float, int>? AwakenEvent;

    internal void RaiseAwaken(float arg1, int arg2) => AwakenEvent?.Invoke(arg1, arg2);
}
