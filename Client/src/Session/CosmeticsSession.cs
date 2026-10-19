namespace LibreKO.Session;

public sealed class CosmeticsSession
{
    public event Action<int, bool>? HelmetEvent;

    internal void RaiseHelmet(int arg1, bool arg2) => HelmetEvent?.Invoke(arg1, arg2);

    public bool HelmetHidden { get; private set; }

    public bool CostumeHidden { get; private set; }

    internal void ApplyOwnLook(bool helmetHidden, bool costumeHidden)
    {
        HelmetHidden = helmetHidden;
        CostumeHidden = costumeHidden;
    }

    public event Action<int, bool>? CostumeEvent;

    internal void RaiseCostume(int arg1, bool arg2) => CostumeEvent?.Invoke(arg1, arg2);
}
