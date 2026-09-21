namespace LibreKO.Game.World;

public static class GmMode
{
    public const int DamageTaken = 1;

    public static bool IsActive(UserSession session) => session.IsGM && session.GmModeEnabled;

    public static int Dealt(UserSession attacker, int targetHp, int damage) =>
        IsActive(attacker) ? Math.Max(damage, targetHp) : damage;

    public static int Taken(UserSession victim, int damage) =>
        damage > 0 && IsActive(victim) ? Math.Min(damage, DamageTaken) : damage;
}
