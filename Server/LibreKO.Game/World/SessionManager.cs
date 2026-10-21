using System.Collections.Concurrent;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public class SessionManager
{
    private readonly ConcurrentDictionary<int, UserSession> sessionsByCharacterId = new();
    private readonly ConcurrentDictionary<Guid, UserSession> sessionsByClientId = new();
    public RegionManager Regions { get; } = new();
    public PartyManager Parties { get; } = new();
    public KnightsManager Knights { get; } = new();
    public BattleZoneManager Battle { get; } = new();
    public MapManager? Maps { get; set; }

    public UserSession? GetByCharacterId(int characterId)
    {
        sessionsByCharacterId.TryGetValue(characterId, out var session);
        return session;
    }

    public UserSession? GetByClientId(Guid clientId)
    {
        sessionsByClientId.TryGetValue(clientId, out var session);
        return session;
    }

    public UserSession CreateSession(IClient client, int characterId, int accountId)
    {
        var session = new UserSession(client, characterId, accountId);
        sessionsByCharacterId[characterId] = session;
        sessionsByClientId[client.Id] = session;
        session.PartyLeaderChanged += BroadcastPartyLeader;
        session.BuffsChanged += RaiseBuffsChanged;
        return session;
    }

    public void RegisterSession(UserSession session)
    {
        sessionsByCharacterId[session.CharacterId] = session;
        sessionsByClientId[session.Client.Id] = session;
        session.PartyLeaderChanged -= BroadcastPartyLeader;
        session.PartyLeaderChanged += BroadcastPartyLeader;
        session.BuffsChanged -= RaiseBuffsChanged;
        session.BuffsChanged += RaiseBuffsChanged;
    }

    public event Action<UserSession>? BuffsChanged;

    private void RaiseBuffsChanged(UserSession session) => BuffsChanged?.Invoke(session);

    private void BroadcastPartyLeader(UserSession session) =>
        _ = Regions.SendToRegion(session, MovementPacketWriter.StateChange(
            session.CharacterId, (byte)StateChangeType.PartyLeader, session.IsPartyLeader ? 1 : 0), excludeSender: false);

    public void RemoveSession(UserSession session)
    {
        session.GenieTime.Pause();
        Regions.RemoveFromRegion(session);
        // Compare-and-remove: only evict if this exact session is still registered, so a
        // lagging cleanup of an old session can't remove a newer one that took over the
        // same character (login takeover).
        sessionsByCharacterId.TryRemove(new KeyValuePair<int, UserSession>(session.CharacterId, session));
        sessionsByClientId.TryRemove(new KeyValuePair<Guid, UserSession>(session.Client.Id, session));
    }

    public UserSession? GetByName(string name)
    {
        foreach (var session in sessionsByCharacterId.Values)
            if (string.Equals(session.Name, name, StringComparison.OrdinalIgnoreCase))
                return session;
        return null;
    }

    public int OnlineCount => sessionsByCharacterId.Count;

    public IEnumerable<UserSession> GetAll() => sessionsByCharacterId.Values;

    // A stall that closed is looked at afresh once it opens again.
    public void ForgetStall(UserSession merchant)
    {
        merchant.Trade.EndStall();
        merchant.Trade.MerchantViewerId = -1;
        foreach (var looker in GetAll())
        {
            if (looker.Trade.MerchantTargetUserId == merchant.CharacterId)
                looker.Trade.MerchantTargetUserId = -1;
        }
    }

    public NpcInstance[] GetActiveAiNpcsSnapshot()
    {
        var activeNpcs = new List<NpcInstance>();
        var seenNpcIds = new HashSet<int>();

        foreach (var session in sessionsByCharacterId.Values)
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
        foreach (var session in sessionsByCharacterId.Values)
            tasks.Add(session.Client.SendPacket(pkt));
        return tasks.Count > 0 ? Task.WhenAll(tasks) : Task.CompletedTask;
    }
}
