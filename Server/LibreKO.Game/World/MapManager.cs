using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class MapManager(ILogger<MapManager> logger)
{
    private readonly Dictionary<short, SmdFile> _zoneMaps = [];
    private readonly Dictionary<short, Dictionary<short, GameEventData>> _zoneEvents = [];

    private const int WarpGroupZoneStride = 10;

    public void LoadAll(IGameDataService gameData, string mapDirectory)
    {
        int loaded = 0;

        foreach (var (zoneId, zoneInfo) in gameData.ZoneInfoTable)
        {
            var smdName = zoneInfo.SmdName?.Trim();
            if (string.IsNullOrEmpty(smdName))
                continue;

            var filePath = Path.Combine(mapDirectory, smdName);

            var smd = SmdFile.Load(filePath);
            if (smd == null)
            {
                logger.LogWarning("Failed to load map {SmdName} for zone {ZoneId}", smdName, zoneId);
                continue;
            }

            _zoneMaps[zoneId] = smd;
            loaded++;
        }

        logger.LogInformation("Loaded {Count}/{Total} zone maps from {Dir}",
            loaded, gameData.ZoneInfoTable.Count, mapDirectory);

        // Load zone events from EVENT table
        int eventCount = 0;
        foreach (var (zoneId, _) in gameData.ZoneInfoTable)
        {
            var events = gameData.GameEventsByZone[(byte)zoneId];
            var dict = new Dictionary<short, GameEventData>();
            foreach (var ev in events)
            {
                dict[ev.EventNum] = ev;
                eventCount++;
            }
            if (dict.Count > 0)
                _zoneEvents[zoneId] = dict;
        }
        logger.LogInformation("Loaded {Count} zone events across {ZoneCount} zones",
            eventCount, _zoneEvents.Count);
    }

    public SmdFile? GetMap(short zoneId)
    {
        return _zoneMaps.GetValueOrDefault(zoneId);
    }

    public float GetHeight(short zoneId, float x, float z)
    {
        var map = GetMap(zoneId);
        return map?.GetHeight(x, z) ?? 0f;
    }

    public bool IsValidPosition(short zoneId, float x, float z)
    {
        var map = GetMap(zoneId);
        return map?.IsValidPosition(x, z) ?? true; // allow if map not loaded
    }

    public bool IsMovable(short zoneId, float x, float z)
    {
        var map = GetMap(zoneId);
        if (map == null) return true; // allow if map not loaded
        return map.GetEventIdAtPosition(x, z) == 0;
    }

    public int GetEventId(short zoneId, float x, float z)
    {
        var map = GetMap(zoneId);
        return map?.GetEventIdAtPosition(x, z) ?? -1;
    }

    public ObjectEvent? GetObjectEvent(short zoneId, int objectIndex)
    {
        var map = GetMap(zoneId);
        return map?.ObjectEvents.GetValueOrDefault(objectIndex);
    }

    public IReadOnlyCollection<ObjectEvent> GetObjectEvents(short zoneId)
    {
        var map = GetMap(zoneId);
        return map?.ObjectEvents.Values.ToArray() ?? [];
    }

    public WarpInfo? GetWarp(short zoneId, int warpId)
    {
        var map = GetMap(zoneId);
        return map?.Warps.GetValueOrDefault(warpId);
    }

    public IReadOnlyList<WarpInfo> GetWarpList(short zoneId, int warpGroup)
    {
        var map = GetMap(zoneId);
        if (map == null)
            return [];

        return [.. map.Warps.Values
            .Where(warp => warp.WarpId / 10 == warpGroup)
            .OrderBy(warp => warp.WarpId)];
    }

    public IReadOnlyList<WarpInfo> GetWarpGateList(short zoneId, ObjectEvent gate)
    {
        var warps = GetWarpList(zoneId, gate.ControlNpcId);
        if (warps.Count > 0 || gate.Belong == 0)
            return warps;

        var zoneGroup = zoneId * WarpGroupZoneStride + gate.ControlNpcId % WarpGroupZoneStride;
        var rebased = GetWarpList(zoneId, zoneGroup);
        if (rebased.Count == 0 || rebased.Any(warp => warp.Nation != gate.Belong))
            return warps;

        logger.LogDebug(
            "Warp gate {Index} in zone {Zone} names group {Group}, which the map has no warps for; using zone group {ZoneGroup} ({Count} warps)",
            gate.Index, zoneId, gate.ControlNpcId, zoneGroup, rebased.Count);
        return rebased;
    }

    public GameEventData? GetZoneEvent(short zoneId, short eventNum)
    {
        if (_zoneEvents.TryGetValue(zoneId, out var events))
            return events.GetValueOrDefault(eventNum);
        return null;
    }

    public GameEventData? CheckEvent(short zoneId, float x, float z)
    {
        var map = GetMap(zoneId);
        if (map == null) return null;

        int eventId = map.GetEventIdAtPosition(x, z);
        if (eventId < 2) return null; // 0 = no event, 1 = reserved

        return GetZoneEvent(zoneId, (short)eventId);
    }

    public int LoadedMapCount => _zoneMaps.Count;
}
