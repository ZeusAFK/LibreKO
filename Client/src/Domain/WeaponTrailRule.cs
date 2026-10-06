namespace LibreKO.Domain;

public static class WeaponTrailRule
{
    public const int Normal = 0;
    public const int Fire = 1;
    public const int Ice = 2;
    public const int Lightning = 3;
    public const int Poison = 4;
    public const int Count = 5;

    public const float AfterimageAlphaStep = 12f;
    public const float AfterimageStepRate = 30f;
    public const float AlphaScale = 255f;
    public const float AfterimageFadePerSecond = AfterimageAlphaStep * AfterimageStepRate / AlphaScale;

    public static int Element(int fire, int ice, int lightning, int poison) =>
        fire != 0 ? Fire
        : ice != 0 ? Ice
        : lightning != 0 ? Lightning
        : poison != 0 ? Poison
        : Normal;
}
