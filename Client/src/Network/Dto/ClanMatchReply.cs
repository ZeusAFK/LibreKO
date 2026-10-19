namespace LibreKO.Network;

public readonly record struct ClanMatchPair(int Zone, int ClanA, int ClanB);

public readonly record struct ClanMatchFighter(int Clan, int Id, string Name, int MaxHp, int Hp, int Class, int MaxMp, int Mp, int Nation);

public readonly record struct ClanMatchVitals(int Id, int MaxHp, int Hp, int MaxMp, int Mp);

public static class ClanMatchReply
{
    private const int FighterFixedBytes = 2 + 4 + 2 + 2 + 2 + 1 + 2 + 2 + 2 + 1;
    private const int VitalsBytes = 4 + 4 * 2;

    public static ClanMatchPair? ReadPair(Packet p)
    {
        if (p.RemainingBytes < 2 + 6) return null;
        int zone = p.ReadShort();
        int clanA = ReadSide(p);
        if (p.RemainingBytes < 6) return null;
        int clanB = ReadSide(p);
        return new ClanMatchPair(zone, clanA, clanB);
    }

    private static int ReadSide(Packet p)
    {
        int clan = p.ReadShort();
        p.ReadShort();
        p.ReadByte();
        p.ReadByte();
        if (p.RemainingBytes >= 1) p.ReadSByteString();
        if (p.RemainingBytes >= 1) p.ReadSByteString();
        return clan;
    }

    public static List<ClanMatchFighter> ReadRoster(Packet p)
    {
        var fighters = new List<ClanMatchFighter>();
        if (p.RemainingBytes < 2) return fighters;
        int count = p.ReadUShort();
        for (int i = 0; i < count && p.RemainingBytes >= FighterFixedBytes; i++)
        {
            int clan = p.ReadShort();
            int id = p.ReadInt();
            string name = p.ReadString();
            int maxHp = p.ReadShort();
            int hp = p.ReadShort();
            p.ReadByte();
            int cls = p.ReadShort();
            int maxMp = p.ReadShort();
            int mp = p.ReadShort();
            int nation = p.ReadByte();
            fighters.Add(new ClanMatchFighter(clan, id, name, maxHp, hp, cls, maxMp, mp, nation));
        }
        return fighters;
    }

    public static ClanMatchVitals? ReadVitals(Packet p) => p.RemainingBytes >= VitalsBytes
        ? new ClanMatchVitals(p.ReadInt(), p.ReadShort(), p.ReadShort(), p.ReadShort(), p.ReadShort())
        : null;
}

public enum ClanMatchKind : byte
{
    Result = 1,
    Roster = 2,
    Vitals = 3,
}
