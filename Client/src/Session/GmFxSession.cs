namespace LibreKO.Session;

public sealed class GmFxSession
{
    private readonly Dictionary<int, bool> states = new();

    public bool Visible(int id, bool isGm) => isGm && states.GetValueOrDefault(id, true);

    internal void Set(int id, bool enabled) => states[id] = enabled;

    internal void Forget(int id) => states.Remove(id);

    internal void Clear() => states.Clear();

    public event Action<int, bool>? GmFxEvent;

    internal void RaiseGmFx(int arg1, bool arg2) => GmFxEvent?.Invoke(arg1, arg2);
}
