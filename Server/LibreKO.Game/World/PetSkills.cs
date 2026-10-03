namespace LibreKO.Game.World;

public static class PetSkills
{
    public const int DesignatedAttack = 301_000;
    public const int FirstSkill = 301_000;
    public const int LastSkill = 400_000;
    public const int SharedPageFirst = 1000;
    public const int SharedPageLast = 1009;
    public const int ClassPageDivisor = 10;
    public const short SatisfactionPerSkill = 10;
    public const byte OwnerMoral = 33;

    public static bool BelongsTo(int skillId, int page, int petClass) =>
        skillId is >= FirstSkill and <= LastSkill
        && (page is >= SharedPageFirst and <= SharedPageLast || page / ClassPageDivisor == petClass);

    public static bool Knows(int skillId, int page, int requiredLevel, int petClass, int petLevel) =>
        BelongsTo(skillId, page, petClass) && requiredLevel <= petLevel;
}
