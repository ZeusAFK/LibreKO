namespace LibreKO.Domain;

public static class GenieClip
{
    public const double Melee = 0.85;
    public const double Archery = 1.2;

    public static double Scale(int rightKind, int leftKind) =>
        IsArcheryKind(rightKind) || IsArcheryKind(leftKind) ? Archery : Melee;

    private static bool IsArcheryKind(int kind) =>
        kind is WeaponAnimation.Bow or WeaponAnimation.Crossbow or WeaponAnimation.LongBow;
}
