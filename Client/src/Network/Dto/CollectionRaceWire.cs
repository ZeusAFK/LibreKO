namespace LibreKO.Network;

public enum CollectionRaceSubOpcode : byte
{
    State = 1,
    Progress = 2,
    Completed = 3,
    Close = 4,
    Winners = 5,
}

public enum CollectionRaceObjectiveKind : byte
{
    Monster = 0,
    EnemyPlayer = 1,
    Item = 2,
}

public class CollectionRaceObjective
{
    public CollectionRaceObjectiveKind Kind;
    public int TargetId;
    public int Count;
    public int Current;
    public string Name = string.Empty;
}

public struct CollectionRaceReward
{
    public int ItemId;
    public int ItemCount;
    public string Name;
    public byte Rate;
}

public class CollectionRaceState
{
    public int RaceId;
    public string Name = string.Empty;
    public byte ZoneId;
    public int RemainingSeconds;
    public bool IsCompleted;
    public int Winners;
    public int MaxWinners;
    public List<CollectionRaceObjective> Objectives = [];
    public List<CollectionRaceReward> Rewards = [];
}
