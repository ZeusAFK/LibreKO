namespace LibreKO.Domain;

public static class GenieApproach
{
    public static bool ShouldMove(bool huntInPlace, bool basicAttack, bool basicInReach, bool hasAttackSkill, bool attackSkillInReach) =>
        !huntInPlace && (basicAttack ? !basicInReach : hasAttackSkill && !attackSkillInReach);
}
