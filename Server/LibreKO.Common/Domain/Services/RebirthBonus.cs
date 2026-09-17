namespace LibreKO.Common.Domain.Services;

public sealed record RebirthBonus(byte Strength, byte Stamina, byte Dexterity, byte Intelligence, byte Magic)
{
    public const int PointsPerRebirth = 2;
    public const int MaxRebirthLevel = 10;

    public static readonly RebirthBonus None = new(0, 0, 0, 0, 0);

    public int Total => Strength + Stamina + Dexterity + Intelligence + Magic;

    public bool IsEmpty => Total == 0;

    public static int PointsFor(int rebirthLevel) =>
        Math.Max(0, Math.Min(rebirthLevel, MaxRebirthLevel)) * PointsPerRebirth;

    public static long RequiredExperience(long levelExperience, int rebirthLevel) =>
        rebirthLevel <= 0 ? levelExperience : levelExperience * (rebirthLevel + 1);
}
