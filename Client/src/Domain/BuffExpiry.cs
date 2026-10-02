namespace LibreKO.Domain;

public static class BuffExpiry
{
    public const int DamageOverTimeCured = 200;
    public const int HealOverTimeEnded = 100;

    public static bool Removes(int code, int buffType, int type1, int timeDamage) => code switch
    {
        DamageOverTimeCured => type1 == MagicType.DotHeal && timeDamage < 0,
        HealOverTimeEnded => type1 == MagicType.DotHeal && timeDamage > 0,
        _ => code != 0 && buffType == code,
    };
}
