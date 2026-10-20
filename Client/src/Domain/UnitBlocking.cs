using Godot;

namespace LibreKO.Domain;

public static class UnitBlocking
{
    public const float Reach = 32f;
    public const float HeightShare = 2.5f;
    public const float PlayerHeight = 1.8f;

    public const int PlayersPassZone = 89;

    public static readonly IReadOnlySet<int> NpcBlockingZones = new HashSet<int> { 17, 19, 20, 32, 33, 69, 81, 82, 83, 90, 93, 95, 96 };

    public static readonly IReadOnlySet<int> NeverBlockingNpcs = new HashSet<int> { 32008, 32080, 32081, 30091, 30092 };

    public static readonly IReadOnlySet<int> BlockingWithoutHostilityNpcs = new HashSet<int> { 30088, 30089, 30090, 30094, 30200 };

    public static readonly IReadOnlySet<int> ThornTraps = new HashSet<int> { 30350, 30351 };

    public static bool PlayerCanBlock(bool hostile, int zone) => hostile && zone != PlayersPassZone;

    public static bool NpcCanBlock(int npcId, bool hostile, int myNation, int npcNation, int zone)
    {
        if (NeverBlockingNpcs.Contains(npcId)) return false;
        if (!hostile && !BlockingWithoutHostilityNpcs.Contains(npcId)) return false;
        return NpcBlockingZones.Contains(zone) || (ThornTraps.Contains(npcId) && FromTheOtherNation(myNation, npcNation));
    }

    public static bool FromTheOtherNation(int myNation, int theirNation) => myNation switch
    {
        Nations.Karus => theirNation == Nations.ElMorad,
        Nations.ElMorad => theirNation == Nations.Karus,
        _ => true,
    };

    public static bool Blocks(Vector2 from, Vector2 to, Vector2 unit, float myHeight, float unitHeight)
    {
        float limit = (myHeight + unitHeight) / HeightShare;
        float after = to.DistanceTo(unit);
        return after < limit && after < from.DistanceTo(unit);
    }
}
