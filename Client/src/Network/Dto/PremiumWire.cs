namespace LibreKO.Network;

public enum PremiumSubOpcode : byte
{
    List = 1,
}

public static class PremiumWire
{
    private const int EntryBytes = 3;
    private const int BonusBytes = 3;
    private const int StatusNone = 0;
    private const int StatusActive = 1;

    public static (int Status, int Type, int Hours)? ReadList(Packet p)
    {
        if (p.RemainingBytes < 2 || (PremiumSubOpcode)p.ReadByte() != PremiumSubOpcode.List) return null;
        int count = p.ReadByte();
        var hoursByType = new Dictionary<int, int>();
        for (int i = 0; i < count && p.RemainingBytes >= EntryBytes; i++)
        {
            int type = p.ReadByte();
            hoursByType[type] = p.ReadUShort();
        }
        int inUse = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        int hours = count > 0 && hoursByType.TryGetValue(inUse, out int left) ? left : 0;
        return hours > 0 ? (StatusActive, inUse, hours) : (StatusNone, 0, 0);
    }

    public static IReadOnlyList<PremiumBonus> ReadBonuses(Packet p)
    {
        if (p.RemainingBytes < 1) return Array.Empty<PremiumBonus>();
        int count = p.ReadByte();
        var bonuses = new List<PremiumBonus>(count);
        for (int i = 0; i < count && p.RemainingBytes >= BonusBytes; i++)
            bonuses.Add(new PremiumBonus((PremiumBonusKind)p.ReadByte(), p.ReadShort()));
        return bonuses;
    }
}
