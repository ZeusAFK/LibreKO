namespace LibreKO.Domain;

public readonly struct GatherRegion
{
    public readonly int ZoneId;
    public readonly float MinX, MinZ, MaxX, MaxZ;

    public GatherRegion(int zoneId, float minX, float minZ, float maxX, float maxZ)
    {
        ZoneId = zoneId; MinX = minX; MinZ = minZ; MaxX = maxX; MaxZ = maxZ;
    }

    public bool Contains(int zoneId, float koX, float koZ)
        => ZoneId == zoneId && koX >= MinX && koX <= MaxX && koZ >= MinZ && koZ <= MaxZ;
}

public static class GatherZones
{
    public const int ZoneKarus = 1;
    public const int ZoneElmorad = 2;
    public const int ZoneMoradon = 21;

    private static readonly GatherRegion[] Mining =
    {
        new(ZoneMoradon, 600f, 348f, 666f, 399f),
        new(ZoneElmorad, 1408f, 354f, 1488f, 440f),
        new(ZoneElmorad, 1653f, 526f, 1733f, 625f),
        new(ZoneKarus, 597f, 1625f, 720f, 1705f),
        new(ZoneKarus, 315f, 1435f, 415f, 1500f),
    };

    private static readonly GatherRegion[] Fishing =
    {
        new(ZoneElmorad, 850f, 1080f, 935f, 1115f),
        new(ZoneKarus, 1106f, 915f, 1209f, 944f),
    };

    public static bool IsMiningArea(int zoneId, float koX, float koZ) => In(Mining, zoneId, koX, koZ);

    public static bool IsFishingArea(int zoneId, float koX, float koZ) => In(Fishing, zoneId, koX, koZ);

    private static bool In(GatherRegion[] regions, int zoneId, float koX, float koZ)
    {
        foreach (var r in regions)
            if (r.Contains(zoneId, koX, koZ)) return true;
        return false;
    }
}
