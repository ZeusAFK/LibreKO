namespace LibreKO.Network;

public enum FishingHallSubOpcode : byte
{
    List = 1,
}

public struct FishingHallEntry
{
    public int Rank;
    public string Name;
    public int Score;
}
