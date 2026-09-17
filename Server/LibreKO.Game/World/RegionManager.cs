using System.Collections.Concurrent;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class RegionManager
{
    public const float RegionSize = 48.0f;
    public const int ViewDistance = 1; // ±1 neighbors (3x3 grid)
    public const long NoRegionKey = -1;

    // Key: packed zoneId|regionX|regionZ -> set of session IDs in that region
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, UserSession>> _regions = new();

    // NPC instances: uniqueId -> NpcInstance
    private readonly ConcurrentDictionary<int, NpcInstance> _npcs = new();
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, NpcInstance>> _npcRegions = new();
    private int _nextNpcId = 10000; // NPC IDs start at 10000 to avoid collision with character IDs

    // NPCs that need AI ticks even without nearby players (in combat, returning, damaged, etc.)
    private readonly ConcurrentDictionary<int, NpcInstance> _engagedNpcs = new();

    // Ground loot bundles: bundleId -> LootBundle
    private readonly ConcurrentDictionary<int, LootBundle> _bundles = new();
    private int _nextBundleId = 1;

    private static long RegionKey(byte zoneId, int rx, int rz) =>
        ((long)zoneId << 32) | ((long)(rx & 0xFFFF) << 16) | (long)(rz & 0xFFFF);

    public void AddToRegion(UserSession session)
    {
        var key = RegionKey(session.ZoneId, session.NewRegionX, session.NewRegionZ);
        var region = _regions.GetOrAdd(key, _ => new ConcurrentDictionary<int, UserSession>());
        region[session.CharacterId] = session;
        session.RegionX = session.NewRegionX;
        session.RegionZ = session.NewRegionZ;
        session.RegisteredRegionKey = key;
    }

    // Keyed by where the session was registered, never recomputed: a zone change moves both.
    public void RemoveFromRegion(UserSession session)
    {
        var key = session.RegisteredRegionKey;
        if (key == NoRegionKey) return;
        if (_regions.TryGetValue(key, out var region))
            region.TryRemove(new KeyValuePair<int, UserSession>(session.CharacterId, session));
        session.RegisteredRegionKey = NoRegionKey;
    }

    public bool UpdateRegion(UserSession session)
    {
        var newRx = session.NewRegionX;
        var newRz = session.NewRegionZ;

        if (session.RegionX == newRx && session.RegionZ == newRz)
            return false;

        RemoveFromRegion(session);
        AddToRegion(session);
        return true;
    }

    public IEnumerable<UserSession> GetNearbyUsers(UserSession session)
        => GetNearbyUsersAt(session.ZoneId, session.RegionX, session.RegionZ, session.CharacterId);

    public IEnumerable<UserSession> GetNearbyUsersAt(byte zoneId, int regionX, int regionZ, int excludeCharacterId)
    {
        for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
        {
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
            {
                var key = RegionKey(zoneId, regionX + dx, regionZ + dz);
                if (_regions.TryGetValue(key, out var region))
                {
                    foreach (var kvp in region)
                    {
                        if (kvp.Key != excludeCharacterId)
                            yield return kvp.Value;
                    }
                }
            }
        }
    }

    public Task SendToRegion(UserSession sender, Packet packet, bool excludeSender = true)
    {
        // SendPacket enqueues and returns synchronously (a background writer does the
        // socket I/O), so there's nothing to await. Fire-and-forget avoids allocating a
        // List<Task> + WhenAll per broadcast — millions of allocs/sec under dense load.
        foreach (var nearby in GetNearbyUsers(sender))
            _ = nearby.Client.SendPacket(packet);

        if (!excludeSender)
            _ = sender.Client.SendPacket(packet);

        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────
    // NPC management
    // ─────────────────────────────────────────────
    public NpcInstance SpawnNpc(NpcInstance npc)
    {
        npc.UniqueId = Interlocked.Increment(ref _nextNpcId);
        _npcs[npc.UniqueId] = npc;
        AddNpcToRegion(npc);
        return npc;
    }

    private void AddNpcToRegion(NpcInstance npc)
    {
        var key = RegionKey(npc.ZoneId, npc.NewRegionX, npc.NewRegionZ);
        var region = _npcRegions.GetOrAdd(key, _ => new ConcurrentDictionary<int, NpcInstance>());
        region[npc.UniqueId] = npc;
        npc.RegionX = npc.NewRegionX;
        npc.RegionZ = npc.NewRegionZ;
    }

    public NpcInstance? GetNpc(int uniqueId)
    {
        _npcs.TryGetValue(uniqueId, out var npc);
        return npc;
    }

    public NpcInstance? GetNpcByProtoId(byte zoneId, short npcId)
    {
        foreach (var npc in _npcs.Values)
        {
            if (npc.ZoneId == zoneId && npc.NpcId == npcId)
                return npc;
        }
        return null;
    }

    public IEnumerable<NpcInstance> GetNearbyNpcs(UserSession session)
    {
        for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
        {
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
            {
                var key = RegionKey(session.ZoneId, session.RegionX + dx, session.RegionZ + dz);
                if (_npcRegions.TryGetValue(key, out var region))
                {
                    foreach (var kvp in region)
                        yield return kvp.Value;
                }
            }
        }
    }

    public IEnumerable<UserSession> GetNearbyUsersForNpc(NpcInstance npc)
    {
        for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
        {
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
            {
                var key = RegionKey(npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (_regions.TryGetValue(key, out var region))
                {
                    foreach (var kvp in region)
                        yield return kvp.Value;
                }
            }
        }
    }

    public bool HasNearbyUsers(NpcInstance npc)
    {
        for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
        {
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
            {
                var key = RegionKey(npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (_regions.TryGetValue(key, out var region) && !region.IsEmpty)
                    return true;
            }
        }
        return false;
    }

    public Task BroadcastFromNpc(NpcInstance npc, Packet packet)
    {
        // Fire-and-forget enqueue (see SendToRegion): avoids a List<Task>+WhenAll per NPC
        // move, which at hundreds of NPCs × hundreds of nearby players is a huge alloc sink.
        for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
        {
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
            {
                var key = RegionKey(npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (_regions.TryGetValue(key, out var region))
                {
                    foreach (var kvp in region)
                        _ = kvp.Value.Client.SendPacket(packet);
                }
            }
        }

        return Task.CompletedTask;
    }

    public void UpdateNpcRegion(NpcInstance npc)
    {
        if (npc.NewRegionX == npc.RegionX && npc.NewRegionZ == npc.RegionZ)
            return;

        // Remove from old region
        var oldKey = RegionKey(npc.ZoneId, npc.RegionX, npc.RegionZ);
        if (_npcRegions.TryGetValue(oldKey, out var oldRegion))
            oldRegion.TryRemove(npc.UniqueId, out _);

        // Add to new region
        AddNpcToRegion(npc);
    }

    public IEnumerable<NpcInstance> GetNearbyNpcsForNpc(NpcInstance npc)
    {
        for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
        {
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
            {
                var key = RegionKey(npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (_npcRegions.TryGetValue(key, out var region))
                {
                    foreach (var kvp in region)
                        yield return kvp.Value;
                }
            }
        }
    }

    public IEnumerable<NpcInstance> GetAllNpcsInZone(byte zoneId)
    {
        return _npcs.Values.Where(n => n.ZoneId == zoneId);
    }

    public IEnumerable<NpcInstance> GetAllNpcs() => _npcs.Values;

    public IEnumerable<NpcInstance> GetAllAliveMonsters()
    {
        return _npcs.Values.Where(n => n.IsAlive && n.HasAi && NpcWorldFilter.ShouldSpawnNormally(n));
    }

    public void MarkNpcEngaged(NpcInstance npc) => _engagedNpcs[npc.UniqueId] = npc;

    public void MarkNpcIdle(NpcInstance npc) => _engagedNpcs.TryRemove(npc.UniqueId, out _);

    public IEnumerable<NpcInstance> GetEngagedNpcs() => _engagedNpcs.Values;

    public void DropAggroOn(int characterId)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        foreach (var npc in _engagedNpcs.Values)
        {
            if (npc.TargetUserId != characterId)
                continue;

            npc.TargetUserId = 0;
            npc.IsTracing = false;
            npc.IsMoving = false;

            if (npc.State == NpcState.Casting && !npc.HealTargetIsNpc)
            {
                npc.ActiveSkillId = 0;
                npc.ActiveTargetId = 0;
                npc.CastEndTicks = 0;
            }

            if (npc.State is NpcState.Attacking or NpcState.Fighting or NpcState.Casting)
            {
                npc.State = NpcState.Returning;
                npc.StateChangeTicks = nowTicks;
            }
        }
    }

    public IEnumerable<NpcInstance> GetDeadNpcsReadyToRespawn(long nowTicks)
    {
        return _npcs.Values.Where(n =>
            n.IsDead
            && n.CanRespawn
            && (nowTicks - n.DeathTimeTicks) >= TimeSpan.FromMilliseconds(n.RespawnDelayMs).Ticks);
    }

    // ─────────────────────────────────────────────
    // Ground loot bundle management
    // ─────────────────────────────────────────────
    public LootBundle CreateBundle(float x, float z, float y)
    {
        var bundle = new LootBundle
        {
            BundleId = Interlocked.Increment(ref _nextBundleId),
            X = x,
            Z = z,
            Y = y,
            DropTimeTicks = DateTime.UtcNow.Ticks
        };
        _bundles[bundle.BundleId] = bundle;
        return bundle;
    }

    public LootBundle? GetBundle(int bundleId)
    {
        _bundles.TryGetValue(bundleId, out var bundle);
        return bundle;
    }

    public void RemoveBundle(int bundleId)
    {
        _bundles.TryRemove(bundleId, out _);
    }

    public bool TryClaimBundleSlot(int bundleId, int slotIndex, int expectedItemId, out LootItem? claimed)
    {
        claimed = null;
        if (!_bundles.TryGetValue(bundleId, out var bundle))
            return false;
        if (!bundle.TryClaimSlot(slotIndex, expectedItemId, out claimed))
            return false;
        if (bundle.IsEmpty())
            _bundles.TryRemove(bundleId, out _);
        return true;
    }

    public void CleanupExpiredBundles()
    {
        var now = DateTime.UtcNow.Ticks;
        foreach (var kvp in _bundles)
        {
            if (kvp.Value.IsExpired(now))
                _bundles.TryRemove(kvp.Key, out _);
        }
    }
}
