namespace LibreKO.Network;

public readonly record struct SiegeWarStatus(
    SiegeWarState State, int OwnerClan, int OwnerMark, int OwnerAlliance, int HoldSeconds, int WarSeconds, string OwnerName)
{
    private const int FixedBytes = 1 + 3 * sizeof(short) + sizeof(int) + sizeof(short);

    public static readonly SiegeWarStatus None = new(SiegeWarState.None, 0, 0, 0, 0, 0, "");

    public bool Running => State is >= SiegeWarState.Unheld and <= SiegeWarState.LastRunning;
    public bool Held => State is > SiegeWarState.Unheld and <= SiegeWarState.LastRunning;

    public static SiegeWarStatus? Read(Packet p)
    {
        if (p.RemainingBytes < FixedBytes) return null;
        var state = (SiegeWarState)p.ReadByte();
        int clan = p.ReadShort();
        int mark = p.ReadShort();
        int alliance = p.ReadShort();
        int hold = p.ReadInt();
        int war = p.ReadShort();
        string name = p.RemainingBytes >= 1 ? p.ReadSByteString() : "";
        return new SiegeWarStatus(state, clan, mark, alliance, hold, war, name);
    }
}

public readonly record struct SiegePoints(SiegePointsKind Kind, int Gained, int Total)
{
    private const byte Valid = 1;

    public static SiegePoints? Read(Packet p, int lastTotal)
    {
        if (p.RemainingBytes < 2 || p.ReadByte() != Valid) return null;
        var kind = (SiegePointsKind)p.ReadByte();
        int baseline = kind == SiegePointsKind.CreditedAtEnd && p.RemainingBytes >= sizeof(int) ? p.ReadInt() : lastTotal;
        if (p.RemainingBytes < sizeof(int)) return null;
        int total = p.ReadInt();
        return new SiegePoints(kind, total - baseline, total);
    }
}

public enum SiegeWarState : byte
{
    None = 0,
    Unheld = 1,
    Held = 2,
    LastRunning = 3,
    Ended = 4,
}

public enum SiegePointsKind : byte
{
    Obtained = 1,
    CreditedAtEnd = 2,
    Renewed = 3,
}
