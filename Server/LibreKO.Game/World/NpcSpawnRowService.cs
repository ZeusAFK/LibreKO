using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public interface INpcSpawnRowService
{
    NpcPosData? Find(int rowIndex);
    IReadOnlyList<NpcInstance> Spawn(NpcPosData pos);
    void Edit(NpcPosData pos, int x, int z, int direction, byte count, short respawn, short range);
    NpcPosData LastPersisted(NpcPosData pos);
    void MarkPersisted(NpcPosData pos);
    Task<int> RespawnRowAsync(NpcPosData pos);
    int AliveCount(NpcPosData pos);
}

public sealed class NpcSpawnRowService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IMonsterAggressionPolicy monsterAggressionPolicy) : INpcSpawnRowService
{
    private const int SpawnPlacementRetries = 32;

    private readonly ConcurrentDictionary<int, NpcPosData> _lastPersisted = new();

    public NpcPosData? Find(int rowIndex) =>
        rowIndex > 0 ? gameData.NpcPositions.FirstOrDefault(position => position.Index == rowIndex) : null;

    public IReadOnlyList<NpcInstance> Spawn(NpcPosData pos)
    {
        var proto = gameData.GetSpawnProto(pos);
        if (proto == null)
            return [];

        var count = pos.NumNPC > 1 ? pos.NumNPC : 1;
        var spawned = new List<NpcInstance>(count);
        for (var i = 0; i < count; i++)
        {
            var npc = NpcInstance.FromData(proto, pos, 0);
            monsterAggressionPolicy.Apply(npc);
            if (sessionManager.Maps != null)
            {
                PlaceOnWalkableGround(npc, pos);
                var height = sessionManager.Maps.GetHeight(npc.ZoneId, npc.X, npc.Z);
                npc.Y = height;
                npc.SpawnY = height;
            }

            sessionManager.Regions.SpawnNpc(npc);
            spawned.Add(npc);
        }

        return spawned;
    }

    public void Edit(NpcPosData pos, int x, int z, int direction, byte count, short respawn, short range)
    {
        _lastPersisted.TryAdd(pos.Index, CloneForZone(pos, pos.ZoneId));
        pos.LeftX = x;
        pos.TopZ = z;
        pos.Direction = direction;
        pos.NumNPC = count;
        pos.RegTime = respawn;
        pos.SpawnRange = range;
    }

    public NpcPosData LastPersisted(NpcPosData pos) =>
        _lastPersisted.TryGetValue(pos.Index, out var persisted) ? persisted : CloneForZone(pos, pos.ZoneId);

    public void MarkPersisted(NpcPosData pos) => _lastPersisted.TryRemove(pos.Index, out _);

    public async Task<int> RespawnRowAsync(NpcPosData pos)
    {
        var live = sessionManager.Regions.GetAllNpcs()
            .Where(npc => npc.SpawnRow == pos.Index && npc.Room == 0)
            .ToList();
        var zones = live.Select(npc => npc.ZoneId).Distinct().ToList();
        if (!zones.Contains((byte)pos.ZoneId))
            zones.Add((byte)pos.ZoneId);

        foreach (var npc in live)
        {
            await sessionManager.Regions.BroadcastFromNpc(npc, NpcPacketMapper.BuildInOutPacket(npc, InOutType.Out));
            sessionManager.Regions.RemoveNpc(npc);
        }

        var spawned = 0;
        foreach (var zone in zones)
        {
            var source = zone == pos.ZoneId ? pos : CloneForZone(pos, zone);
            foreach (var npc in Spawn(source))
            {
                await sessionManager.Regions.BroadcastFromNpc(npc, NpcPacketMapper.BuildInOutPacket(npc, InOutType.In));
                spawned++;
            }
        }

        return spawned;
    }

    public int AliveCount(NpcPosData pos) =>
        sessionManager.Regions.GetAllNpcs().Count(npc => npc.SpawnRow == pos.Index && npc.ZoneId == pos.ZoneId && !npc.IsDead);

    public void PlaceOnWalkableGround(NpcInstance npc, NpcPosData pos)
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

    public static NpcPosData CloneForZone(NpcPosData source, short zoneId)
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
}
