namespace LibreKO.Session;

public sealed class SeasonalSession
{
    public event Action<int>? SantaEvent;

    internal void RaiseSanta(int value) => SantaEvent?.Invoke(value);
}
