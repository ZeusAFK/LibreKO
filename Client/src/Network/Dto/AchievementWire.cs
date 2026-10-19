namespace LibreKO.Network;

public enum AchievementSubOpcode : byte
{
    ClaimResult = 2,
    List = 3,
    Summary = 4,
    ClaimReward = 6,
    SelectDisplayTitle = 16,
    SelectSkillTitle = 17,
    ResetDisplayTitle = 18,
    ResetSkillTitle = 19,
    TitleChanged = 32,
}

public enum AchievementState : byte
{
    InProgress = 0,
    Achieved = 4,
    Claimed = 5,
}

public enum AchievementClaimResult : int
{
    ItemMissing = -2,
    InventoryFull = -1,
    NotAvailable = 0,
    Issued = 1,
}

public enum AchievementTitleResult : int
{
    SelectCooldown = -5,
    ResetCooldown = -1,
    Done = 1,
}

public static class AchievementWire
{
    public const int RecentSlots = 3;
    public const int TabCount = 5;
    public const int ScaledTarget = ushort.MaxValue;
}

public struct AchievementSummary
{
    public int PlayMinutes;
    public int MonstersDefeated;
    public int PlayersDefeated;
    public int Deaths;
    public int Points;
    public int[] RecentlyAchieved;
    public int[] AchievedPerTab;
}

public struct AchievementEntry
{
    public int Id;
    public AchievementState State;
    public int Progress;
    public int Target;

    public bool Claimable => State == AchievementState.Achieved;
    public bool Claimed => State == AchievementState.Claimed;
    public float Fraction => Target > 0 ? Math.Clamp((float)Progress / Target, 0f, 1f) : 0f;

    public string ProgressText => Target == AchievementWire.ScaledTarget
        ? $"{Fraction * 100f:0.#}%"
        : $"{Progress} / {Target}";
}
