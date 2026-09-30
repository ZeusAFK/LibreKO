namespace LibreKO.Domain;

public enum MonsterStoneResult : byte
{
    CannotEnterHere = 1,
    Entered = 2,
    PartyLeaderOnly = 4,
    Failed = 5,
    Refused = 7,
    PartyLevelTooHigh = 8,
    NotEnoughHealth = 9,
}

public static class NestDungeon
{
    public const int EventSummonGroup = 1;
    public const string InPartyRefusal = "You cannot use it while in a party.";
    public const string CompletedLine = "Quest Mission Completed!!!";

    private const string TerminationLabel = "Mission termination time....";
    private const int FirstNest = 81;
    private const int SecondNest = 82;
    private const int ThirdNest = 83;
    private const int BorderDefenseWar = 84;
    private const int JuraidMountain = 87;
    private const int SecondsPerMinute = 60;
    private const int SecondsPerHour = 3600;

    public static string DeadRefusal(string itemName) => $"{itemName} cannot be used while dead.";

    public static bool IsNestZone(int zone) => zone is FirstNest or SecondNest or ThirdNest;

    public static bool IsInstancedZone(int zone) =>
        IsNestZone(zone) || zone is BorderDefenseWar or JuraidMountain;

    public static string TerminationLine(int secondsLeft, bool completed)
    {
        if (completed) return $"{TerminationLabel} {secondsLeft} seconds";
        int hours = secondsLeft / SecondsPerHour;
        int minutes = secondsLeft % SecondsPerHour / SecondsPerMinute;
        int seconds = secondsLeft % SecondsPerMinute;
        return $"{TerminationLabel}{hours:00}:{minutes:00}:{seconds:00}";
    }

    public static string? Message(MonsterStoneResult result, int currentZone) => result switch
    {
        MonsterStoneResult.CannotEnterHere => IsInstancedZone(currentZone)
            ? "You are already in an instanced dungeon."
            : "You cannot enter from this zone.",
        MonsterStoneResult.PartyLeaderOnly => "Only the party leader can request entry.",
        MonsterStoneResult.Failed or MonsterStoneResult.Refused => "Failed to enter the dungeon.",
        MonsterStoneResult.PartyLevelTooHigh => "Only a party whose members are all below level 75 can enter.",
        MonsterStoneResult.NotEnoughHealth => "You cannot use it with less than half of your HP.",
        _ => null,
    };
}
