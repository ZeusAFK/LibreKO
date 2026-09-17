using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public readonly record struct RegenSubject(
    byte Level,
    short Hp,
    short MaxHp,
    short Mp,
    short MaxMp,
    short Class,
    byte ZoneId,
    bool IsSitting,
    bool IsGameMaster,
    bool SnowWarOpen);

public readonly record struct RegenAmounts(int Hp, int Mp);

public static class VitalsRegenCalculator
{
    public const int IntervalSeconds = 5;

    private const int SnowWarHpPerTick = 5;
    private const int MageLowMpPercent = 30;
    private const int MageMpBonusPercent = 120;
    private const int NormalPercent = 100;
    private const int PrisonSittingMpPercent = 5;
    private const int SittingMpNumerator = 5;
    private const int SittingMpLevelOffset = 30;
    private const int SittingMpFlat = 3;
    private const int SittingHpLevelDivisor = 30;
    private const int SittingHpFlat = 3;
    private const int StandingMpLevelDivisor = 60;
    private const double StandingMpScale = 0.2;
    private const int StandingMpFlat = 3;

    public static RegenAmounts Calculate(in RegenSubject subject)
    {
        if (subject.Hp <= 0)
            return default;

        if (subject.ZoneId == (byte)ZoneId.SnowBattle && subject.SnowWarOpen)
            return new RegenAmounts(SnowWarHpPerTick, 0);

        int mpPercent = HasMageLowMpBonus(subject) ? MageMpBonusPercent : NormalPercent;

        return subject.IsSitting
            ? SittingAmounts(subject, mpPercent)
            : new RegenAmounts(0, StandingMp(subject, mpPercent));
    }

    private static bool HasMageLowMpBonus(in RegenSubject subject) =>
        ClassIdHelper.GetSubtype(subject.Class) == (int)ClassSubtype.MageMastered
        && subject.Mp < subject.MaxMp * MageLowMpPercent / 100;

    private static RegenAmounts SittingAmounts(in RegenSubject subject, int mpPercent)
    {
        if (subject.IsGameMaster)
            return new RegenAmounts(subject.MaxHp, subject.MaxMp);

        int level = subject.Level;
        int hp = (int)(level * (1 + level / (double)SittingHpLevelDivisor)) + SittingHpFlat;

        int mp = subject.ZoneId == (byte)ZoneId.Prison && level > 1
            ? subject.MaxMp * PrisonSittingMpPercent / 100
            : ((subject.MaxMp * SittingMpNumerator / (level - 1 + SittingMpLevelOffset))
                + SittingMpFlat) * mpPercent / 100;

        return new RegenAmounts(hp, mp);
    }

    private static int StandingMp(in RegenSubject subject, int mpPercent)
    {
        int level = subject.Level;
        return (int)(((level * (1 + level / (double)StandingMpLevelDivisor) + 1) * StandingMpScale)
            + StandingMpFlat) * mpPercent / 100;
    }
}
