namespace LibreKO.Domain;

public static class SkillFxTarget
{
    public static bool IsTerrain(int targetPart) => targetPart is 254 or 255;

    public static bool MatchesPacket(int targetPart, int targetId) =>
        IsTerrain(targetPart) ? targetId == -1 : targetId >= 0;
}
