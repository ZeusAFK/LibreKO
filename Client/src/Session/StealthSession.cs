namespace LibreKO.Session;

public sealed class StealthSession
{
    public event Action<int, InvisibilityType>? StealthEvent;

    internal void RaiseStealth(int arg1, InvisibilityType arg2) => StealthEvent?.Invoke(arg1, arg2);

    public event Action<float>? SightEvent;

    internal void RaiseSight(float value) => SightEvent?.Invoke(value);
}
