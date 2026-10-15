namespace LibreKO.Domain;

public sealed class ShownRebuild
{
    public bool Owed { get; private set; }

    public bool Changed(bool shown)
    {
        Owed = !shown;
        return shown;
    }

    public bool Shown()
    {
        bool owed = Owed;
        Owed = false;
        return owed;
    }

    public void Rebuilt() => Owed = false;
}
