namespace LibreKO.Domain;

public enum GroundEffect : byte { None, Hay, Swamp, DriedRiver, SwampSlow }

public static class GroundEffects
{
    public const float SlowFactor = 0.7f;
    public const int SwampAuraFx = 32161;

    public static GroundEffect? Read(ZoneTerrainSubOpcode sub, byte effect) =>
        sub == ZoneTerrainSubOpcode.Terrain && effect <= (byte)GroundEffect.SwampSlow ? (GroundEffect)effect : null;

    public static float SpeedFactor(GroundEffect effect, float current) => effect switch
    {
        GroundEffect.None => 1f,
        GroundEffect.Swamp or GroundEffect.SwampSlow => SlowFactor,
        _ => current,
    };

    public static bool HasAura(GroundEffect effect) => effect == GroundEffect.SwampSlow;

    public static (int Id, string Fallback) Text(GroundEffect effect) => effect switch
    {
        GroundEffect.Hay => (44720, "Terrain: Hay - Boosts Fire magic, but weakens Ice and Lightening damage."),
        GroundEffect.Swamp => (44721, "Terrain: Swamp - Boosts all magic damage but decreases character speed."),
        GroundEffect.DriedRiver => (44722, "Terrain: Water - Weakens Fire magic but boosts Ice and Lightening damage."),
        GroundEffect.SwampSlow => (44723, "Terrain: Swamp - Decreases character speed."),
        _ => (0, ""),
    };

    public static string? IconPath(GroundEffect effect) => effect switch
    {
        GroundEffect.Hay => "res://assets/ui/ground/hay.png",
        GroundEffect.Swamp or GroundEffect.SwampSlow => "res://assets/ui/ground/swamp.png",
        GroundEffect.DriedRiver => "res://assets/ui/ground/water.png",
        _ => null,
    };
}
