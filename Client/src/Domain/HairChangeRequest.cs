namespace LibreKO.Domain;

public sealed class HairChangeRequest
{
    public bool Pending { get; private set; }
    public int Face { get; private set; }
    public int Hair { get; private set; }

    public bool TryBegin(int face, int hair)
    {
        if (Pending || face is < byte.MinValue or > byte.MaxValue) return false;
        Pending = true;
        Face = face;
        Hair = hair;
        return true;
    }

    public bool TryFinish()
    {
        if (!Pending) return false;
        Pending = false;
        return true;
    }
}
