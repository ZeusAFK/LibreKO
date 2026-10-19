namespace LibreKO.Network;

public enum ForcesSubOpcode : byte
{
    Status = 1,
    Join = 2,
    Leave = 3,
}

public struct ForcesStatus
{
    public bool Joined;
    public int Points;
    public byte Rank;
    public byte AngerPct;
}
