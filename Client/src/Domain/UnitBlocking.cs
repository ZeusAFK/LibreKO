using Godot;

namespace LibreKO.Domain;

public static class UnitBlocking
{
    public const float Reach = 32f;
    public const float HeightShare = 2.5f;
    public const float PlayerHeight = 1.8f;

    public static bool CanBlock(bool attackable, bool isMonster, int npcType) =>
        attackable && (!isMonster || npcType == NpcTypes.GuardSummon);

    public static bool Blocks(Vector2 from, Vector2 to, Vector2 unit, float myHeight, float unitHeight)
    {
        float limit = (myHeight + unitHeight) / HeightShare;
        float after = to.DistanceTo(unit);
        return after < limit && after < from.DistanceTo(unit);
    }
}
