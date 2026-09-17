namespace LibreKO.Common.Domain.Services;

public readonly record struct BaseStatBlock(
    byte Strength,
    byte Stamina,
    byte Dexterity,
    byte Intelligence,
    byte Magic);

public static class ProgressionTable
{
    public const byte MinLevel = 1;
    public const byte MaxLevel = 83;

    public const int MasterySlotCount = 9;
    public const int MasteryPoolSlot = 0;
    public const int MasteryFirstLevel = 10;
    public const int MasteryClassFirstSlot = 5;
    public const int MasteryMasterSlot = MasterySlotCount - 1;
    public const int MasteryClassSlotCount = MasterySlotCount - MasteryClassFirstSlot;
    public const int MasteryMasterLevel = 60;
    public const int MasteryMasterMaxPoints = MaxLevel - MasteryMasterLevel;
    public const int StatBonusLevel = 60;
    public const int BaseStatTotal = 290;
    public const byte StatMax = 255;

    public const int StatPointsPerLevel = 3;
    public const int BonusStatPointsPerLevel = 2;
    public const int MasteryPointsPerLevel = 2;

    private const int StartingStatPoints = 10;

    public static bool IsValidLevel(int level) => level is >= MinLevel and <= MaxLevel;

    public static short StatPointsForLevel(int level)
    {
        var clamped = Math.Clamp(level, MinLevel, MaxLevel);
        var points = StartingStatPoints + (clamped - MinLevel) * StatPointsPerLevel;
        if (clamped > StatBonusLevel)
            points += BonusStatPointsPerLevel * (clamped - StatBonusLevel);
        return (short)points;
    }

    public static byte MasteryPointsForLevel(int level)
    {
        var clamped = Math.Clamp(level, MinLevel, MaxLevel);
        var points = (clamped - MasteryFirstLevel + 1) * MasteryPointsPerLevel;
        return (byte)Math.Clamp(points, 0, byte.MaxValue);
    }

    public static BaseStatBlock BaseStatsForClass(int classId) => (Math.Abs(classId) % 100) switch
    {
        1 or 5 or 6 => new BaseStatBlock(65, 65, 60, 50, 50),
        2 or 7 or 8 => new BaseStatBlock(60, 60, 70, 50, 50),
        3 or 9 or 10 => new BaseStatBlock(50, 50, 70, 70, 50),
        4 or 11 or 12 => new BaseStatBlock(50, 60, 60, 70, 50),
        13 or 14 or 15 => new BaseStatBlock(65, 65, 60, 50, 50),
        _ => new BaseStatBlock(58, 58, 58, 58, 58),
    };
}
