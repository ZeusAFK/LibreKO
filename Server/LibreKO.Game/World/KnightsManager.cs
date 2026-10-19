using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public class KnightsManager
{
    public const int ClanWarehouseSlots = 192;

    private readonly ConcurrentDictionary<int, KnightsEntity> clans = new();
    private readonly ConcurrentDictionary<int, ItemSlot[]> clanWarehouseCache = new();

    public void AddClan(int clanId, KnightsEntity clan) => clans[clanId] = clan;

    public KnightsEntity? GetClan(int clanId)
    {
        clans.TryGetValue(clanId, out var clan);
        return clan;
    }

    public void RemoveClan(int clanId)
    {
        clans.TryRemove(clanId, out _);
        clanWarehouseCache.TryRemove(clanId, out _);
    }

    public IEnumerable<KnightsEntity> GetAll() => clans.Values;

    public int Count => clans.Count;

    public ItemSlot[]? GetClanWarehouse(int clanId)
    {
        if (!clans.TryGetValue(clanId, out var clan))
            return null;

        return clanWarehouseCache.GetOrAdd(clanId, _ =>
        {
            var slots = new ItemSlot[ClanWarehouseSlots];
            for (var i = 0; i < ClanWarehouseSlots; i++)
                slots[i] = new ItemSlot();
            UserSessionBinaryState.LoadWarehouse(slots, clan.ClanWarehouseItems);
            return slots;
        });
    }

    public byte[] SerializeClanWarehouse(int clanId)
    {
        if (!clanWarehouseCache.TryGetValue(clanId, out var slots))
            return [];
        return UserSessionBinaryState.SerializeWarehouse(slots);
    }

    private readonly ConcurrentDictionary<int, Lock> clanWarehouseLocks = new();

    public T WithClanWarehouse<T>(int clanId, Func<KnightsEntity, ItemSlot[], T> mutator, T fallback)
    {
        var lockObj = clanWarehouseLocks.GetOrAdd(clanId, _ => new Lock());
        using var scope = lockObj.EnterScope();
        if (!clans.TryGetValue(clanId, out var clan))
            return fallback;
        var slots = GetClanWarehouse(clanId);
        if (slots == null)
            return fallback;
        return mutator(clan, slots);
    }

    // Alliance tracking: maps clanId → alliance.
    private readonly ConcurrentDictionary<int, KnightsAllianceEntity> allianceByClan = new();

    public void LoadAlliances(IEnumerable<KnightsAllianceEntity> alliances)
    {
        allianceByClan.Clear();
        foreach (var alliance in alliances)
            IndexAlliance(alliance);
    }

    public void AddAlliance(KnightsAllianceEntity alliance) => IndexAlliance(alliance);

    public void UpdateAlliance(KnightsAllianceEntity alliance)
    {
        // Drop every cache entry that pointed to this alliance.
        foreach (var key in allianceByClan.Keys.ToList())
        {
            if (allianceByClan.TryGetValue(key, out var existing)
                && existing.MainClanId == alliance.MainClanId)
            {
                allianceByClan.TryRemove(key, out _);
            }
        }
        IndexAlliance(alliance);
    }

    public void RemoveAlliance(short mainClanId)
    {
        foreach (var key in allianceByClan.Keys.ToList())
        {
            if (allianceByClan.TryGetValue(key, out var existing)
                && existing.MainClanId == mainClanId)
            {
                allianceByClan.TryRemove(key, out _);
            }
        }
    }

    public KnightsAllianceEntity? GetAllianceForClan(int clanId)
    {
        allianceByClan.TryGetValue(clanId, out var alliance);
        return alliance;
    }

    public IEnumerable<short> GetAllianceClanIds(int clanId)
    {
        var alliance = GetAllianceForClan(clanId);
        return alliance?.GetAllClanIds() ?? [];
    }

    private void IndexAlliance(KnightsAllianceEntity alliance)
    {
        foreach (var clanId in alliance.GetAllClanIds())
            allianceByClan[clanId] = alliance;
    }
}
