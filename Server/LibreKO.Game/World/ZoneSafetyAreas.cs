using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public enum ZoneRegionShape : byte
{
    Box = 0,
    Circle = 1,
}

public readonly record struct ZoneRegion(
    ZoneRegionShape Shape, float X, float Z, float Radius, float MaxX, float MaxZ)
{
    public static ZoneRegion Box(float minX, float minZ, float maxX, float maxZ)
        => new(ZoneRegionShape.Box, minX, minZ, 0f, maxX, maxZ);

    public static ZoneRegion Circle(float x, float z, float radius)
        => new(ZoneRegionShape.Circle, x, z, radius, 0f, 0f);

    public bool Contains(float x, float z)
        => Shape == ZoneRegionShape.Circle
            ? ((x - X) * (x - X)) + ((z - Z) * (z - Z)) <= Radius * Radius
            : x >= X && x <= MaxX && z >= Z && z <= MaxZ;
}

public readonly record struct SafetyArea(byte ZoneId, AccountNation Owner, ZoneRegion Region);

public static class ZoneSafetyAreas
{
    private static readonly SafetyArea[] Areas =
    [
        new((byte)ZoneId.Delos, AccountNation.None, ZoneRegion.Circle(500f, 180f, 115f)),
        new((byte)ZoneId.Arena, AccountNation.None, ZoneRegion.Circle(127f, 113f, 36f)),

        new((byte)ZoneId.KarusCamp1, AccountNation.ElMorad, ZoneRegion.Circle(1860f, 174f, 50f)),
        new((byte)ZoneId.KarusCamp2, AccountNation.ElMorad, ZoneRegion.Circle(1860f, 174f, 50f)),
        new((byte)ZoneId.ElMoradCamp1, AccountNation.Karus, ZoneRegion.Circle(210f, 1853f, 50f)),
        new((byte)ZoneId.ElMoradCamp2, AccountNation.Karus, ZoneRegion.Circle(210f, 1853f, 50f)),

        new((byte)ZoneId.Bifrost, AccountNation.Karus, ZoneRegion.Box(56f, 700f, 124f, 840f)),
        new((byte)ZoneId.Bifrost, AccountNation.ElMorad, ZoneRegion.Box(190f, 870f, 270f, 970f)),

        new((byte)ZoneId.NapiesGorge, AccountNation.ElMorad, ZoneRegion.Box(98f, 755f, 125f, 780f)),
        new((byte)ZoneId.NapiesGorge, AccountNation.Karus, ZoneRegion.Box(805f, 85f, 831f, 110f)),

        new((byte)ZoneId.AlseidsPrairie, AccountNation.ElMorad, ZoneRegion.Box(942f, 863f, 977f, 904f)),
        new((byte)ZoneId.AlseidsPrairie, AccountNation.Karus, ZoneRegion.Box(46f, 142f, 80f, 174f)),

        new((byte)ZoneId.NereidsIsland, AccountNation.ElMorad, ZoneRegion.Circle(235f, 228f, 80f)),
        new((byte)ZoneId.NereidsIsland, AccountNation.ElMorad, ZoneRegion.Circle(846f, 362f, 20f)),
        new((byte)ZoneId.NereidsIsland, AccountNation.ElMorad, ZoneRegion.Circle(338f, 807f, 20f)),
        new((byte)ZoneId.NereidsIsland, AccountNation.Karus, ZoneRegion.Circle(809f, 783f, 80f)),
        new((byte)ZoneId.NereidsIsland, AccountNation.Karus, ZoneRegion.Circle(182f, 668f, 20f)),
        new((byte)ZoneId.NereidsIsland, AccountNation.Karus, ZoneRegion.Circle(670f, 202f, 20f)),
    ];

    public static bool IsInOwnSafetyArea(UserSession session)
    {
        foreach (var area in Areas)
        {
            if (!Covers(area, session))
                continue;

            if (area.Owner == AccountNation.None || area.Owner == session.Nation)
                return true;
        }

        return false;
    }

    public static bool IsInEnemySafetyArea(UserSession session)
    {
        foreach (var area in Areas)
        {
            if (Covers(area, session) && area.Owner != session.Nation)
                return true;
        }

        return false;
    }

    private static bool Covers(SafetyArea area, UserSession session)
        => area.ZoneId == session.ZoneId && area.Region.Contains(session.X, session.Z);
}
