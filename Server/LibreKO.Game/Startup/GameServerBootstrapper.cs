using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Startup;

public interface IGameServerBootstrapper
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public class GameServerBootstrapper(
    IGameDataService gameDataService,
    SessionManager sessionManager,
    MapManager mapManager,
    IAccountLockService accountLockService,
    IServiceScopeFactory scopeFactory,
    IHostEnvironment hostEnvironment,
    IOptions<GameServerSettings> settings,
    IMonsterAggressionPolicy monsterAggressionPolicy,
    ILogger<GameServerBootstrapper> logger) : IGameServerBootstrapper
{
    private const short ObjectBind = 0;
    private const short ObjectGate = 1;
    private const short ObjectGateLever = 3;
    private const short ObjectRemoveBind = 7;

    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await _initializeLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
                return;

            await gameDataService.LoadAsync(cancellationToken);
            InitializeMaps(gameDataService);
            SpawnNpcs(gameDataService);
            SpawnObjectEventNpcs(gameDataService);
            await LoadClansAndAlliancesAsync(cancellationToken);
            await accountLockService.ClearOwnClaimsAsync();

            LogPublicDemoGrants();

            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private void LogPublicDemoGrants()
    {
        var demo = settings.Value.PublicDemo;
        var granted = new List<string>();
        if (demo.GrantGameMasterPanelToEveryone) granted.Add("GM panel");
        if (demo.GrantGameMasterSpeedToEveryone) granted.Add("GM speed");
        if (demo.GrantSetLevelToEveryone) granted.Add("+setlevel");
        if (granted.Count == 0)
            return;

        logger.LogWarning(
            "PUBLIC DEMO MODE: every account gets {Grants}. Turn off GameServer:PublicDemo before launch (docs/PUBLIC_DEMO_GM_PANEL.md).",
            string.Join(" + ", granted));
    }

    private void InitializeMaps(IGameDataService gameData)
    {
        var mapDir = GameAssetPathResolver.ResolveExistingDirectory(
            settings.Value.MapDirectory,
            hostEnvironment.ContentRootPath,
            "Map");

        if (!string.IsNullOrEmpty(mapDir))
        {
            logger.LogInformation("Loading maps from {MapDirectory}", mapDir);
            mapManager.LoadAll(gameData, mapDir);
            sessionManager.Maps = mapManager;
            return;
        }

        logger.LogWarning(
            "Map directory not configured or not found. Candidates: {Dirs}. Map features disabled.",
            string.Join(", ", GameAssetPathResolver.GetCandidateDirectories(
                settings.Value.MapDirectory,
                hostEnvironment.ContentRootPath,
                "Map")));
    }

    private void SpawnNpcs(IGameDataService gameData)
    {
        var spawned = 0;
        var skipped = 0;
        var perZone = new Dictionary<byte, int>();
        var positionsByZone = gameData.NpcPositions.ToLookup(position => position.ZoneId);

        logger.LogInformation("NpcPositions count: {Count}, NPC data count: {NpcCount}",
            gameData.NpcPositions.Count, gameData.MonsterTable.Count + gameData.NpcTable.Count);

        foreach (var zoneId in gameData.ZoneInfoTable.Keys.OrderBy(zone => zone))
        {
            var sourceZone = ResolveSharedMapNpcZone(gameData, sessionManager.Maps, zoneId, positionsByZone);
            foreach (var sourcePosition in positionsByZone[sourceZone])
            {
                if (sourcePosition.Room != 0)
                    continue;

                var pos = sourcePosition.ZoneId == zoneId
                    ? sourcePosition
                    : ClonePositionForZone(sourcePosition, zoneId);

                var npcData = gameData.GetSpawnProto(pos);
                if (npcData == null)
                {
                    skipped++;
                    logger.LogDebug("NPC ID {NpcId} in zone {Zone} has no {Side} data - skipped",
                        pos.NpcId, pos.ZoneId,
                        pos.ActType < NpcPosData.NpcSpawnActTypeBase ? "monster" : "NPC");
                    continue;
                }

                var count = pos.NumNPC > 1 ? pos.NumNPC : 1;
                for (var i = 0; i < count; i++)
                {
                    var npc = NpcInstance.FromData(npcData, pos, 0);

                    monsterAggressionPolicy.Apply(npc);

                    if (sessionManager.Maps != null)
                    {
                        PlaceOnWalkableGround(npc, pos);

                        var height = sessionManager.Maps.GetHeight(npc.ZoneId, npc.X, npc.Z);
                        npc.Y = height;
                        npc.SpawnY = height;
                    }

                    sessionManager.Regions.SpawnNpc(npc);
                    spawned++;

                    var zone = (byte)pos.ZoneId;
                    perZone[zone] = perZone.GetValueOrDefault(zone) + 1;
                }
            }
        }

        foreach (var kv in perZone.OrderBy(k => k.Key))
            logger.LogInformation("  Zone {Zone}: {Count} NPCs spawned", kv.Key, kv.Value);

        logger.LogInformation("Spawned {Count} NPCs across all zones", spawned);

        if (skipped > 0)
            logger.LogInformation("{SkippedMissing} positions skipped due to missing NPC data", skipped);
    }

    private void SpawnObjectEventNpcs(IGameDataService gameData)
    {
        if (sessionManager.Maps == null)
            return;

        var spawned = 0;

        foreach (var (zoneId, _) in gameData.ZoneInfoTable)
        {
            foreach (var objectEvent in sessionManager.Maps.GetObjectEvents(zoneId))
            {
                if (!ShouldSpawnObjectEventNpc(objectEvent))
                    continue;

                var npcId = ResolveObjectEventNpcId(objectEvent);
                if (npcId <= 0)
                    continue;

                var npcData = gameData.GetNpc(npcId, isMonster: false);
                if (npcData == null)
                {
                    logger.LogDebug("Object event {ObjectIndex} in zone {Zone} resolved to missing NPC {NpcId}",
                        objectEvent.Index, zoneId, npcId);
                    continue;
                }

                var npc = NpcInstance.FromObjectEvent(npcData, objectEvent, zoneId, 0);

                var height = sessionManager.Maps.GetHeight(npc.ZoneId, npc.X, npc.Z);
                npc.Y = height;
                npc.SpawnY = height;

                sessionManager.Regions.SpawnNpc(npc);
                spawned++;
            }
        }

        if (spawned > 0)
            logger.LogInformation("Spawned {Count} object-event NPCs across all zones", spawned);
    }

    internal static bool ShouldSpawnObjectEventNpc(ObjectEvent objectEvent)
        => objectEvent.Type is ObjectBind or ObjectGate or ObjectGateLever or ObjectRemoveBind;

    internal static int ResolveObjectEventNpcId(ObjectEvent objectEvent)
        => ShouldSpawnObjectEventNpc(objectEvent) ? objectEvent.Index : 0;

    internal static short ResolveSharedMapNpcZone(
        IGameDataService gameData,
        MapManager? maps,
        short zoneId,
        ILookup<short, NpcPosData> positionsByZone)
    {
        if (positionsByZone[zoneId].Any())
            return zoneId;

        if (!gameData.ZoneInfoTable.TryGetValue(zoneId, out var currentZone))
            return zoneId;

        var smdName = currentZone.SmdName?.Trim();
        if (string.IsNullOrEmpty(smdName))
            return zoneId;

        var familyName = NormalizeMapFamily(currentZone.MapName);
        var family = gameData.ZoneInfoTable
            .Where(entry => entry.Key != zoneId && positionsByZone[entry.Key].Any())
            .Where(entry => string.Equals(
                NormalizeMapFamily(entry.Value.MapName), familyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key)
            .ToList();

        var sameFile = family.FirstOrDefault(entry => string.Equals(
            entry.Value.SmdName?.Trim(), smdName, StringComparison.OrdinalIgnoreCase));
        if (sameFile.Value != null)
            return sameFile.Key;

        // Karus and El Morad Eslant are one place split across two .smd files that differ only in the
        // event-tile block (each nation's exit tile), so a same-name sibling only counts when its
        // terrain is byte-identical — "Border War Defance" is two genuinely different maps.
        var terrain = maps?.GetMap(zoneId);
        foreach (var entry in family)
            if (SameTerrain(terrain, maps?.GetMap(entry.Key)))
                return entry.Key;

        return zoneId;
    }

    private static bool SameTerrain(SmdFile? a, SmdFile? b)
    {
        if (a == null || b == null || ReferenceEquals(a, b)) return false;
        if (a.MapSize != b.MapSize || a.UnitDistance != b.UnitDistance) return false;
        return a.HeightMap.AsSpan().SequenceEqual(b.HeightMap);
    }

    private const int SpawnPlacementRetries = 32;

    private void PlaceOnWalkableGround(NpcInstance npc, NpcPosData pos)
    {
        var maps = sessionManager.Maps!;
        if (maps.IsMovable(npc.ZoneId, npc.X, npc.Z))
            return;

        for (var attempt = 0; attempt < SpawnPlacementRetries; attempt++)
        {
            var (x, z) = NpcInstance.RandomSpawnPoint(pos);
            if (!maps.IsMovable(npc.ZoneId, x, z))
                continue;

            npc.X = npc.SpawnX = x;
            npc.Z = npc.SpawnZ = z;
            return;
        }

        if (!maps.IsMovable(npc.ZoneId, pos.LeftX, pos.TopZ))
            return;

        npc.X = npc.SpawnX = pos.LeftX;
        npc.Z = npc.SpawnZ = pos.TopZ;
    }

    private static NpcPosData ClonePositionForZone(NpcPosData source, short zoneId)
        => new()
        {
            Index = source.Index,
            ZoneId = zoneId,
            NpcId = source.NpcId,
            ActType = source.ActType,
            DotCnt = source.DotCnt,
            Path = source.Path,
            LeftX = source.LeftX,
            TopZ = source.TopZ,
            NumNPC = source.NumNPC,
            RegTime = source.RegTime,
            Direction = source.Direction,
            SpawnRange = source.SpawnRange,
            RegenType = source.RegenType,
            DungeonFamily = source.DungeonFamily,
            SpecialType = source.SpecialType,
            TrapNumber = source.TrapNumber,
            Room = source.Room,
        };

    private static string NormalizeMapFamily(string mapName)
    {
        var normalized = (mapName ?? string.Empty).Trim();
        foreach (var suffix in SharedMapVariantSuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalized[..^suffix.Length].TrimEnd();
        }

        return normalized;
    }

    private static readonly string[] SharedMapVariantSuffixes =
    [
        " VIII",
        " VII",
        " III",
        " II",
        " IV",
        " VI",
        " IX",
        " V",
        " X",
        " I"
    ];

    private async Task LoadClansAndAlliancesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var clans = await db.Knights.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var clan in clans)
            sessionManager.Knights.AddClan(clan.Id, clan);

        var alliances = await db.KnightsAlliances.AsNoTracking().ToListAsync(cancellationToken);
        sessionManager.Knights.LoadAlliances(alliances);

        logger.LogInformation("Loaded {Count} clans, {AllianceCount} alliances", clans.Count, alliances.Count);
    }
}
