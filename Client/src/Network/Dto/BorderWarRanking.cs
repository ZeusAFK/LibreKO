namespace LibreKO.Network;

public readonly record struct BorderWarRankRow(string Name, int Nation, int ClanId, string ClanName, int Points);

public sealed record BorderWarRanking(
    IReadOnlyList<BorderWarRankRow> Karus,
    IReadOnlyList<BorderWarRankRow> Elmorad,
    uint VictoryExp,
    uint BonusExp)
{
    private const int RowHeadBytes = 1 + 2 + 2;
    private const int TailBytes = 4 + 4;

    public static BorderWarRanking Read(Packet p)
    {
        var karus = ReadNation(p);
        var elmorad = ReadNation(p);
        uint exp = 0, bonus = 0;
        if (p.RemainingBytes >= TailBytes)
        {
            exp = p.ReadUInt();
            bonus = p.ReadUInt();
        }
        return new BorderWarRanking(karus, elmorad, exp, bonus);
    }

    private static List<BorderWarRankRow> ReadNation(Packet p)
    {
        var rows = new List<BorderWarRankRow>();
        if (p.RemainingBytes < 2) return rows;
        int count = p.ReadUShort();
        for (int i = 0; i < count && i < RankWire.BorderWarRowsPerNation; i++)
        {
            if (p.RemainingBytes < 2) break;
            string name = p.ReadString();
            if (p.RemainingBytes < RowHeadBytes) break;
            int nation = p.ReadByte();
            int clanId = p.ReadUShort();
            p.ReadUShort();
            if (p.RemainingBytes < 2) break;
            string clan = p.ReadString();
            if (p.RemainingBytes < 4) break;
            rows.Add(new BorderWarRankRow(name, nation, clanId, clan, p.ReadInt()));
        }
        return rows;
    }
}
