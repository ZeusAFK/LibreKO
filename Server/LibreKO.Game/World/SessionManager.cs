using System.Collections.Concurrent;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class SessionManager
{
    private readonly ConcurrentDictionary<int, UserSession> _sessionsByCharacterId = new();
    private readonly ConcurrentDictionary<Guid, UserSession> _sessionsByClientId = new();
    public RegionManager Regions { get; } = new();
    public PartyManager Parties { get; } = new();
    public KnightsManager Knights { get; } = new();
    public BattleZoneManager Battle { get; } = new();
    public MapManager? Maps { get; set; }

    public UserSession? GetByCharacterId(int characterId)
    {
        _sessionsByCharacterId.TryGetValue(characterId, out var session);
        return session;
    }

    public UserSession? GetByClientId(Guid clientId)
    {
        _sessionsByClientId.TryGetValue(clientId, out var session);
        return session;
    }

    public UserSession CreateSession(IClient client, int characterId, int accountId)
    {
        var session = new UserSession(client, characterId, accountId);
        _sessionsByCharacterId[characterId] = session;
        _sessionsByClientId[client.Id] = session;
        return session;
    }

    public void RegisterSession(UserSession session)
    {
        _sessionsByCharacterId[session.CharacterId] = session;
        _sessionsByClientId[session.Client.Id] = session;
    }

    public void RemoveSession(UserSession session)
    {
        Regions.RemoveFromRegion(session);
        // Compare-and-remove: only evict if this exact session is still registered, so a
        // lagging cleanup of an old session can't remove a newer one that took over the
        // same character (login takeover).
        _sessionsByCharacterId.TryRemove(new KeyValuePair<int, UserSession>(session.CharacterId, session));
        _sessionsByClientId.TryRemove(new KeyValuePair<Guid, UserSession>(session.Client.Id, session));
    }

    public UserSession? GetByName(string name)
    {
        foreach (var session in _sessionsByCharacterId.Values)
            if (string.Equals(session.Name, name, StringComparison.OrdinalIgnoreCase))
                return session;
        return null;
    }

    public int OnlineCount => _sessionsByCharacterId.Count;

    public IEnumerable<UserSession> GetAll() => _sessionsByCharacterId.Values;

    public NpcInstance[] GetActiveAiNpcsSnapshot()
    {
        var activeNpcs = new List<NpcInstance>();
        var seenNpcIds = new HashSet<int>();

        foreach (var session in _sessionsByCharacterId.Values)
        {
            if (session.RegionX < 0 || session.RegionZ < 0)
                continue;

            foreach (var npc in Regions.GetNearbyNpcs(session))
            {
                if (!npc.IsAlive || !npc.HasAi || !NpcWorldFilter.ShouldSpawnNormally(npc))
                    continue;

                if (seenNpcIds.Add(npc.UniqueId))
                    activeNpcs.Add(npc);
            }
        }

        foreach (var npc in Regions.GetEngagedNpcs())
        {
            if (!npc.IsAlive || !npc.HasAi || !NpcWorldFilter.ShouldSpawnNormally(npc))
                continue;

            if (seenNpcIds.Add(npc.UniqueId))
                activeNpcs.Add(npc);
        }

        return [.. activeNpcs];
    }

    public Task BroadcastToAll(Packet pkt)
    {
        var tasks = new List<Task>();
        foreach (var session in _sessionsByCharacterId.Values)
            tasks.Add(session.Client.SendPacket(pkt));
        return tasks.Count > 0 ? Task.WhenAll(tasks) : Task.CompletedTask;
    }
}
