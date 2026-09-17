namespace LibreKO.Game.World;

public readonly record struct GatherRegion(byte ZoneId, float MinX, float MinZ, float MaxX, float MaxZ)
{
    public bool Contains(byte zoneId, float x, float z)
        => ZoneId == zoneId && x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

public static class GatherZones
{
    private static readonly GatherRegion[] MiningRegions =
    [
        new(BattleZoneManager.ZONE_MORADON, 600f, 348f, 666f, 399f),
        new(BattleZoneManager.ZONE_ELMORAD, 1408f, 354f, 1488f, 440f),
        new(BattleZoneManager.ZONE_ELMORAD, 1653f, 526f, 1733f, 625f),
        new(BattleZoneManager.ZONE_KARUS, 597f, 1625f, 720f, 1705f),
        new(BattleZoneManager.ZONE_KARUS, 315f, 1435f, 415f, 1500f),
    ];

    private static readonly GatherRegion[] FishingRegions =
    [
        new(BattleZoneManager.ZONE_ELMORAD, 850f, 1080f, 935f, 1115f),
        new(BattleZoneManager.ZONE_KARUS, 1106f, 915f, 1209f, 944f),
    ];

    public static bool IsMiningArea(byte zoneId, float x, float z) => Contains(MiningRegions, zoneId, x, z);

    public static bool IsFishingArea(byte zoneId, float x, float z) => Contains(FishingRegions, zoneId, x, z);

    public static IReadOnlyList<GatherRegion> Mining => MiningRegions;

    public static IReadOnlyList<GatherRegion> Fishing => FishingRegions;

    private static bool Contains(GatherRegion[] regions, byte zoneId, float x, float z)
    {
        foreach (var region in regions)
        {
            if (region.Contains(zoneId, x, z))
                return true;
        }

        return false;
    }
}
