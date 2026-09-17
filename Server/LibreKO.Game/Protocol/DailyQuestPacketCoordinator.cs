using System.Collections.Generic;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IDailyQuestPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class DailyQuestPacketCoordinator(
    SessionManager sessionManager,
    IUserNotificationService userNotification,
    ILogger<DailyQuestPacketCoordinator> logger) : IDailyQuestPacketCoordinator
{

    // questId -> (title, required level (0 = always available), gold reward)
    private static readonly (int Id, string Title, int Level, int Reward)[] Catalog =
    {
        (1, "Daily — Log in today", 0, 5000),
        (2, "Daily — Slay monsters in the field", 0, 8000),
        (3, "Daily — Reach Level 20", 20, 15000),
        (4, "Daily — Veteran's stipend (Lv 40+)", 40, 30000),
    };

    private static readonly Dictionary<int, HashSet<int>> claimed = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = (DailyQuestSubOpcode)packet.ReadByte();
        switch (sub)
        {
            case DailyQuestSubOpcode.List: await SendListAsync(session); break;
            case DailyQuestSubOpcode.Claim: await HandleClaimAsync(session, packet); break;
        }
    }

    private async Task SendListAsync(UserSession session)
    {
        var done = GetClaimed(session.CharacterId);
        var entries = new List<DailyQuestPacketWriter.Entry>(Catalog.Length);
        foreach (var (id, title, level, _) in Catalog)
        {
            entries.Add(new DailyQuestPacketWriter.Entry(
                id, session.Level >= level, done.Contains(id), title));
        }

        await session.Client.SendPacket(DailyQuestPacketWriter.QuestList(DailyQuestSubOpcode.List, entries));
    }

    private async Task HandleClaimAsync(UserSession session, Packet packet)
    {
        int questId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;
        var entry = System.Array.Find(Catalog, c => c.Id == questId);
        var done = GetClaimed(session.CharacterId);

        if (entry.Id == 0 || session.Level < entry.Level || done.Contains(questId))
        {
            await session.Client.SendPacket(DailyQuestPacketWriter.Result(
                DailyQuestSubOpcode.Claim, DailyQuestPacketWriter.Failed, questId));
            return;
        }

        done.Add(questId);
        session.Money += entry.Reward;
        await userNotification.SendGoldGainAsync(session, entry.Reward);
        logger.LogDebug("{Name} claimed daily quest {Q} (+{Gold} gold)", session.Name, questId, entry.Reward);
        await session.Client.SendPacket(DailyQuestPacketWriter.Result(
            DailyQuestSubOpcode.Claim, DailyQuestPacketWriter.Succeeded, questId));
    }

    private static HashSet<int> GetClaimed(int charId)
    {
        if (!claimed.TryGetValue(charId, out var set))
        {
            set = new HashSet<int>();
            claimed[charId] = set;
        }
        return set;
    }
}
