namespace LibreKO.Domain;

public static class WeaponGlowRule
{
    public const int NoFx = 0;
    public const int ElementalKind = 110;
    public const int VariantSpan = 1000;
    public const int IceOffset = 10;
    public const int LightningOffset = 20;
    public const int TintThreshold = 64;
    public const uint White = 0xFFFFFFFF;
    public const uint FireTint = 0xFFFFFF00;
    public const uint IceTint = 0xFFB4C8FF;
    public const uint LightningTint = 0xFF1E96F0;
    public const uint PoisonTint = 0xFFFF00FF;

    private static readonly int[] AnyAmountGrades = { 4, 6, 11, 12 };

    public static int VariantFx(int glowFx, int kind, int effect2, int linked, int fire, int ice, int lightning)
    {
        int variant = effect2 / VariantSpan;
        if (variant == NoFx || glowFx == NoFx) return NoFx;
        if (kind != ElementalKind) return linked == 0 ? variant : NoFx;
        if (fire == 0 && ice == 0 && lightning == 0) return NoFx;
        if (System.Math.Max(fire, ice) < lightning) return variant + LightningOffset;
        return fire < ice ? variant + IceOffset : variant;
    }

    public static uint Tint(int magicOrRare, int fire, int ice, int lightning, int poison)
    {
        bool anyAmount = System.Array.IndexOf(AnyAmountGrades, magicOrRare) >= 0;
        bool On(int amount) => anyAmount ? amount != 0 : amount >= TintThreshold;
        if (On(fire)) return FireTint;
        if (On(ice)) return IceTint;
        if (On(lightning)) return LightningTint;
        if (On(poison)) return PoisonTint;
        return White;
    }
}
