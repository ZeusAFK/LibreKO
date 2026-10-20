namespace LibreKO.Network;

public enum BountySubOpcode : byte
{
    List = 1,
    Post = 2,
    Claim = 3,
    Withdraw = 4,
}

public enum BountyResult : byte
{
    Failed = 0,
    Succeeded = 1,
}

public readonly record struct BountyOutcome(int BountyId, bool Succeeded);

public enum BountyStanding : byte
{
    Open = 0,
    Own = 1,
    Earned = 2,
}

public struct BountyEntry
{
    public int Id;
    public string Target;
    public string Poster;
    public int Reward;
    public BountyStanding Standing;
}
