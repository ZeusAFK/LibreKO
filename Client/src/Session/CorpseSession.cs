namespace LibreKO.Session;

public sealed class CorpseSession
{
    public event Action<int, float, float, float>? CorpseEvent;

    internal void RaiseCorpse(int arg1, float arg2, float arg3, float arg4) => CorpseEvent?.Invoke(arg1, arg2, arg3, arg4);
}
