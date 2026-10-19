namespace LibreKO.Network;

public enum RankType : byte
{
    PkZone = 1,
    BorderDefenseWar = 2,
    ChaosDungeon = 3,
}

public static class RankWire
{
    public const int BorderWarRowsPerNation = 10;
}
