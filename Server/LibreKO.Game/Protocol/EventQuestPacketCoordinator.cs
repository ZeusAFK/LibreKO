using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IEventQuestPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class EventQuestPacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    ILogger<EventQuestPacketCoordinator> logger) : IEventQuestPacketCoordinator
{
    private const byte EventQuestSubList = 1;
    private const byte EventQuestSubAccept = 2;
    private const byte EventQuestSubClaim = 3;

    // questId -> title. Static limited-time event quest catalog (in-memory; resets on server restart).
    private static readonly (int Id, string Title)[] EventQuestCatalog =
    {
        (9001, "Spring Festival — Slay 10 Goblins"),
        (9002, "Spring Festival — Gather 5 Wild Herbs"),
        (9003, "Founders' Day — Defeat the Bone Captain"),
        (9004, "Founders' Day — Deliver the Sealed Letter"),
        (9005, "Harvest Moon — Catch 3 River Carp"),
        (9006, "Harvest Moon — Light the 4 Beacons"),
    };

    // charId -> accepted event-quest ids (in-memory; resets on server restart).
    private static readonly Dictionary<int, HashSet<int>> eventQuestAccepted = new();
    // charId -> claimed event-quest ids (in-memory; resets on server restart).
    private static readonly Dictionary<int, HashSet<int>> eventQuestClaimed = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case EventQuestSubList:
                await SendEventQuestListAsync(session);
                break;
            case EventQuestSubAccept:
                await HandleEventQuestAcceptAsync(session, packet);
                break;
            case EventQuestSubClaim:
                await HandleEventQuestClaimAsync(session, packet);
                break;
        }
    }

    private async Task SendEventQuestListAsync(UserSession session)
    {
        var accepted = GetEventQuestSet(eventQuestAccepted, session.CharacterId);
        var claimed = GetEventQuestSet(eventQuestClaimed, session.CharacterId);

        var entries = new List<EventQuestPacketWriter.Entry>(EventQuestCatalog.Length);
        foreach (var (id, title) in EventQuestCatalog)
        {
            bool isAccepted = accepted.Contains(id);
            // Claimable once accepted and not yet claimed (objective tracking stubbed).
            bool claimable = isAccepted && !claimed.Contains(id);
            entries.Add(new EventQuestPacketWriter.Entry(id, title, isAccepted, claimable));
        }

        await session.Client.SendPacket(
            EventQuestPacketWriter.QuestList(EventQuestSubList, entries));
    }

    private async Task HandleEventQuestAcceptAsync(UserSession session, Packet packet)
    {

        int questId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        bool known = System.Array.FindIndex(EventQuestCatalog, q => q.Id == questId) >= 0;
        var accepted = GetEventQuestSet(eventQuestAccepted, session.CharacterId);
        var claimed = GetEventQuestSet(eventQuestClaimed, session.CharacterId);

        // Fail if the quest is unknown, already accepted, or already claimed.
        if (!known || accepted.Contains(questId) || claimed.Contains(questId))
        {
            await session.Client.SendPacket(EventQuestPacketWriter.Result(
                EventQuestSubAccept, EventQuestPacketWriter.Failed, questId));
            return;
        }

        accepted.Add(questId);
        logger.LogDebug("{Name} accepted event quest {Quest}", session.Name, questId);
        await session.Client.SendPacket(EventQuestPacketWriter.Result(
            EventQuestSubAccept, EventQuestPacketWriter.Succeeded, questId));
    }

    private async Task HandleEventQuestClaimAsync(UserSession session, Packet packet)
    {

        int questId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        bool known = System.Array.FindIndex(EventQuestCatalog, q => q.Id == questId) >= 0;
        var accepted = GetEventQuestSet(eventQuestAccepted, session.CharacterId);
        var claimed = GetEventQuestSet(eventQuestClaimed, session.CharacterId);

        // Claimable only if accepted and not yet claimed (objective tracking stubbed).
        if (!known || !accepted.Contains(questId) || claimed.Contains(questId))
        {
            await session.Client.SendPacket(EventQuestPacketWriter.Result(
                EventQuestSubClaim, EventQuestPacketWriter.Failed, questId));
            return;
        }

        claimed.Add(questId);
        // Per-quest gold reward: scales with the quest id so later/harder event quests pay more
        // (9001 -> 50000, 9002 -> 60000, ...). Granted exactly once, gated by the claimed-set guard above.
        int reward = 50000 + (questId - 9001) * 10000;
        if (reward < 50000)
            reward = 50000;
        session.Money += reward;
        await userNotification.SendGoldGainAsync(session, reward);   // grants gold + GS_GOLD_CHANGE
        logger.LogDebug("{Name} claimed event quest {Quest} (+{Gold} gold)", session.Name, questId, reward);
        await session.Client.SendPacket(EventQuestPacketWriter.Result(
            EventQuestSubClaim, EventQuestPacketWriter.Succeeded, questId));
    }

    private static HashSet<int> GetEventQuestSet(Dictionary<int, HashSet<int>> store, int charId)
    {
        if (!store.TryGetValue(charId, out var set))
        {
            set = new HashSet<int>();
            store[charId] = set;
        }
        return set;
    }
}
