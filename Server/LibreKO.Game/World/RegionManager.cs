using System.Collections.Concurrent;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class RegionManager
{
    public const float RegionSize = 48.0f;
    public const int ViewDistance = 1; // ±1 neighbors (3x3 grid)
    public const long NoRegionKey = -1;

    // Key: packed zoneId|regionX|regionZ -> set of session IDs in that region
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, UserSession>> regions = new();

    // NPC instances: uniqueId -> NpcInstance
    private readonly ConcurrentDictionary<int, NpcInstance> npcs = new();
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, NpcInstance>> npcRegions = new();
    private int nextNpcId = 10000; // NPC IDs start at 10000 to avoid collision with character IDs

    // NPCs that need AI ticks even without nearby players (in combat, returning, damaged, etc.)
    private readonly ConcurrentDictionary<int, NpcInstance> engagedNpcs = new();

    // Ground loot bundles: bundleId -> LootBundle
    private readonly ConcurrentDictionary<int, LootBundle> bundles = new();
    private int nextBundleId = 1;

    private static long RegionKey(ushort room, byte zoneId, int rx, int rz) =>
        ((long)room << 40) | ((long)zoneId << 32) | ((long)(rx & 0xFFFF) << 16) | (long)(rz & 0xFFFF);

    public void AddToRegion(UserSession session)
    {
        var key = RegionKey(session.Room, session.ZoneId, session.NewRegionX, session.NewRegionZ);
        var region = regions.GetOrAdd(key, _ => new ConcurrentDictionary<int, UserSession>());
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
        if (regions.TryGetValue(key, out var region))
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
        => GetNearbyUsersAt(session.Room, session.ZoneId, session.RegionX, session.RegionZ, session.CharacterId);

    public static bool IsInView(UserSession viewer, UserSession target)
        => viewer.Room == target.Room
        && viewer.ZoneId == target.ZoneId
        && Math.Abs(viewer.RegionX - target.RegionX) <= ViewDistance
        && Math.Abs(viewer.RegionZ - target.RegionZ) <= ViewDistance;

    public IEnumerable<UserSession> GetNearbyUsersAt(ushort room, byte zoneId, int regionX, int regionZ, int excludeCharacterId)
    {
        for (var dx = -ViewDistance; dx <= ViewDistance; dx++)
        {
            for (var dz = -ViewDistance; dz <= ViewDistance; dz++)
            {
                var key = RegionKey(room, zoneId, regionX + dx, regionZ + dz);
                if (regions.TryGetValue(key, out var region))
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
        npc.UniqueId = Interlocked.Increment(ref nextNpcId);
        npcs[npc.UniqueId] = npc;
        AddNpcToRegion(npc);
        return npc;
    }

    private void AddNpcToRegion(NpcInstance npc)
    {
        var key = RegionKey(npc.Room, npc.ZoneId, npc.NewRegionX, npc.NewRegionZ);
        var region = npcRegions.GetOrAdd(key, _ => new ConcurrentDictionary<int, NpcInstance>());
        region[npc.UniqueId] = npc;
        npc.RegionX = npc.NewRegionX;
        npc.RegionZ = npc.NewRegionZ;
    }

    public void RemoveNpc(NpcInstance npc)
    {
        npcs.TryRemove(npc.UniqueId, out _);
        engagedNpcs.TryRemove(npc.UniqueId, out _);
        if (npcRegions.TryGetValue(RegionKey(npc.Room, npc.ZoneId, npc.RegionX, npc.RegionZ), out var region))
            region.TryRemove(npc.UniqueId, out _);
    }

    public NpcInstance? GetNpc(int uniqueId)
    {
        npcs.TryGetValue(uniqueId, out var npc);
        return npc;
    }

    public NpcInstance? GetNpcByProtoId(byte zoneId, short npcId)
    {
        foreach (var npc in npcs.Values)
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
                var key = RegionKey(session.Room, session.ZoneId, session.RegionX + dx, session.RegionZ + dz);
                if (npcRegions.TryGetValue(key, out var region))
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
                var key = RegionKey(npc.Room, npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (regions.TryGetValue(key, out var region))
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
                var key = RegionKey(npc.Room, npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (regions.TryGetValue(key, out var region) && !region.IsEmpty)
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
                var key = RegionKey(npc.Room, npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (regions.TryGetValue(key, out var region))
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
        var oldKey = RegionKey(npc.Room, npc.ZoneId, npc.RegionX, npc.RegionZ);
        if (npcRegions.TryGetValue(oldKey, out var oldRegion))
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
                var key = RegionKey(npc.Room, npc.ZoneId, npc.RegionX + dx, npc.RegionZ + dz);
                if (npcRegions.TryGetValue(key, out var region))
                {
                    foreach (var kvp in region)
                        yield return kvp.Value;
                }
            }
        }
    }

    public IEnumerable<NpcInstance> GetAllNpcsInZone(byte zoneId)
    {
        return npcs.Values.Where(n => n.ZoneId == zoneId);
    }

    public IEnumerable<NpcInstance> GetAllNpcs() => npcs.Values;

    public IEnumerable<NpcInstance> GetAllAliveMonsters()
    {
        return npcs.Values.Where(n => n.IsAlive && n.HasAi && NpcWorldFilter.ShouldSpawnNormally(n));
    }

    public void MarkNpcEngaged(NpcInstance npc) => engagedNpcs[npc.UniqueId] = npc;

    public void MarkNpcIdle(NpcInstance npc) => engagedNpcs.TryRemove(npc.UniqueId, out _);

    public IEnumerable<NpcInstance> GetEngagedNpcs() => engagedNpcs.Values;

    public void DropAggroOn(int characterId)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        foreach (var npc in engagedNpcs.Values)
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
        return npcs.Values.Where(n =>
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
            BundleId = Interlocked.Increment(ref nextBundleId),
            X = x,
            Z = z,
            Y = y,
            DropTimeTicks = DateTime.UtcNow.Ticks
        };
        bundles[bundle.BundleId] = bundle;
        return bundle;
    }

    public LootBundle? GetBundle(int bundleId)
    {
        bundles.TryGetValue(bundleId, out var bundle);
        return bundle;
    }

    public void RemoveBundle(int bundleId)
    {
        bundles.TryRemove(bundleId, out _);
    }

    public bool TryClaimBundleSlot(int bundleId, int slotIndex, int expectedItemId, out LootItem? claimed)
    {
        claimed = null;
        if (!bundles.TryGetValue(bundleId, out var bundle))
            return false;
        if (!bundle.TryClaimSlot(slotIndex, expectedItemId, out claimed))
            return false;
        if (bundle.IsEmpty())
            bundles.TryRemove(bundleId, out _);
        return true;
    }

    public void CleanupExpiredBundles()
    {
        var now = DateTime.UtcNow.Ticks;
        foreach (var kvp in bundles)
        {
            if (kvp.Value.IsExpired(now))
                bundles.TryRemove(kvp.Key, out _);
        }
    }
}
