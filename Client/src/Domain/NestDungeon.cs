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
    public const int TitleText = 7420;
    public const string TitleFallback = "<< %s >>";
    public const int CompletedText = 7472;
    public const string CompletedFallback = "Quest Mission Completed!!!";
    public const int TerminationText = 7449;
    public const string TerminationFallback = "Mission termination time....";
    public const int SecondsText = 7446;
    public const string SecondsFallback = "seconds";
    public const int InPartyText = 9916;
    public const int DeadText = 9918;
    public const int BattleText = 7725;
    public const int ExpQuestionText = 42152;
    public const double BattleSeconds = 10.0;

    private const int ExpFactor = 5;
    private const int ExpBase = 10;

    private const int FirstNest = 81;
    private const int SecondNest = 82;
    private const int ThirdNest = 83;
    private const int SecondsPerMinute = 60;
    private const int SecondsPerHour = 3600;

    public static bool IsNestZone(int zone) => zone is FirstNest or SecondNest or ThirdNest;

    public static bool IsInstancedZone(int zone) =>
        IsNestZone(zone) || zone is TempleZones.BorderDefenseWar or TempleZones.JuraidMountain;

    public static string TerminationLine(int secondsLeft, bool completed, string label = TerminationFallback,
        string seconds = SecondsFallback)
    {
        if (completed) return $"{label} {secondsLeft} {seconds}";
        int hours = secondsLeft / SecondsPerHour;
        int minutes = secondsLeft % SecondsPerHour / SecondsPerMinute;
        int secondsPart = secondsLeft % SecondsPerMinute;
        return $"{label}{hours:00}:{minutes:00}:{secondsPart:00}";
    }

    public static long ExpFor(int level) => ExpFactor * ((long)level * level * level + ExpBase);

    public static NestReply? Reply(MonsterStoneResult result, int currentZone) => result switch
    {
        MonsterStoneResult.CannotEnterHere => IsInstancedZone(currentZone)
            ? new NestReply(9914, "Currently participating another instanced dungeon", true)
            : new NestReply(9911, "Cannot enter in current zone", true),
        MonsterStoneResult.PartyLeaderOnly => new NestReply(9912, "Only Party Leader can request admission to enter", true),
        MonsterStoneResult.Failed or MonsterStoneResult.Refused => new NestReply(9913, "Failed to enter instanced dungeon", true),
        MonsterStoneResult.PartyLevelTooHigh => new NestReply(9915, "Only a party whose members are all below level 75 can enter.", true),
        MonsterStoneResult.NotEnoughHealth => new NestReply(9919, "You can't use when less than half of your hp", false),
        _ => null,
    };
}

public readonly record struct NestReply(int Id, string Fallback, bool Boxed);
