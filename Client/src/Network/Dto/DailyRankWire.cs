namespace LibreKO.Network;

public enum DailyRankSubOpcode : byte
{
    Board = 1,
    Reset = 3,
}

public static class DailyRankWire
{
    public static DailyRankBoardData? ReadBoard(Packet p)
    {
        if (p.RemainingBytes < 5) return null;
        int board = p.ReadByte();
        if (board >= DailyRank.BoardSlots) return null;
        int myPlace = p.ReadInt();
        if (board == DailyRank.AdonisBoard && myPlace == DailyRank.AdonisUnranked) myPlace = 0;
        int change = 0;
        if (DailyRank.HasChange(board))
        {
            if (p.RemainingBytes < 4) return null;
            change = p.ReadInt();
        }
        if (p.RemainingBytes < 2) return null;
        int count = Math.Min((int)p.ReadShort(), DailyRank.MaxNames);
        var entries = new List<DailyRankEntry>();
        for (int i = 0; i < count && p.RemainingBytes >= 1; i++)
        {
            string name = p.ReadSByteString();
            if (name.Length > 0) entries.Add(new DailyRankEntry(i + 1, name));
        }
        return new DailyRankBoardData(board, myPlace, change, entries);
    }
}
