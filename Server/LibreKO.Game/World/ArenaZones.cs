namespace LibreKO.Game.World;

public readonly record struct ArenaRegion(
    byte Id, string Name, float MinX, float MinZ, float MaxX, float MaxZ, float ExitX, float ExitZ)
{
    public bool Contains(float x, float z)
        => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
}

public static class ArenaZones
{
    public const byte NoArena = 0;
    public const byte MoradonPartyArena = 1;
    public const byte MoradonPersonArena = 2;

    private const float MoradonArenaExitX = 747f;
    private const float MoradonArenaExitZ = 427f;

    private static readonly ArenaRegion[] MoradonArenas =
    [
        new(MoradonPartyArena, "Party Arena", 680f, 356f, 736f, 412f,
            MoradonArenaExitX, MoradonArenaExitZ),
        new(MoradonPersonArena, "Personal Arena", 680f, 436f, 736f, 496f,
            MoradonArenaExitX, MoradonArenaExitZ),
    ];

    public static byte GetArenaId(byte zoneId, float x, float z)
    {
        if (zoneId != BattleZoneManager.ZONE_MORADON)
            return NoArena;

        foreach (var arena in MoradonArenas)
        {
            if (arena.Contains(x, z))
                return arena.Id;
        }

        return NoArena;
    }

    public static string GetArenaName(byte arenaId)
    {
        foreach (var arena in MoradonArenas)
        {
            if (arena.Id == arenaId)
                return arena.Name;
        }

        return string.Empty;
    }

    public static bool TryGetExit(byte arenaId, out float x, out float z)
    {
        foreach (var arena in MoradonArenas)
        {
            if (arena.Id != arenaId) continue;
            x = arena.ExitX;
            z = arena.ExitZ;
            return true;
        }

        x = 0f;
        z = 0f;
        return false;
    }

    public static IReadOnlyList<ArenaRegion> ForZone(byte zoneId)
        => zoneId == BattleZoneManager.ZONE_MORADON ? MoradonArenas : [];
}
