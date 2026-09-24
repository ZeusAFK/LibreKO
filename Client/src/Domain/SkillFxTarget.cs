namespace LibreKO.Domain;

public enum ImpactFxPlacement
{
    None,
    OnEntity,
    UnderEntity,
    AtImpactPoint,
}

public static class SkillFxTarget
{
    public const int AreaImpactTarget = -1;

    public static bool IsTerrain(int targetPart) => targetPart is 254 or 255;

    public static ImpactFxPlacement Placement(int targetPart, int targetId, bool areaCast)
    {
        if (!IsTerrain(targetPart))
            return targetId >= 0 ? ImpactFxPlacement.OnEntity : ImpactFxPlacement.None;
        if (targetId == AreaImpactTarget)
            return ImpactFxPlacement.AtImpactPoint;
        return targetId >= 0 && !areaCast ? ImpactFxPlacement.UnderEntity : ImpactFxPlacement.None;
    }
}
