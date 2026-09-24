namespace LibreKO.Domain;

public static class MagicType
{
    public const int None = 0;
    public const int Melee = 1;
    public const int Ranged = 2;
    public const int DotHeal = 3;
    public const int Buff = 4;
    public const int Special = 5;
    public const int Transform = 6;
    public const int Aoe = 7;
    public const int Warp = 8;
    public const int Stealth = 9;
}

public static class WarpType
{
    public const int Blink = 20;
}

public static class MagicSub
{
    public const int Casting = 1;
    public const int Flying = 2;
    public const int Effecting = 3;
    public const int Fail = 4;
    public const int DurationExpired = 5;
    public const int Cancel = 6;
    public const int CancelTransformation = 7;
}

public static class HealTarget
{
    public const int Hp = 1;
    public const int Mp = 2;
}

public static class SpecialMagic
{
    public const int RemoveDot = 1;
    public const int RemoveBuff = 2;
    public const int Resurrect = 3;
    public const int ResurrectSelf = 4;
    public const int RemoveBless = 5;
    public const int LifeCrystal = 6;
}
