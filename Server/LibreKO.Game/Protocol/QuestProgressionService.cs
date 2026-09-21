using LibreKO.Common.Domain.Services;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Quests.Runtime;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol;

public interface IQuestProgressionService
{
    Task HandleQuestAsync(IClient client, Packet packet);
    Task CheckQuestKillAsync(UserSession killer, int npcId);
}

public class QuestProgressionService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IQuestDialogRunner dialogRunner,
    IQuestDefinitionSource objectives,
    ICharacterStatePersister statePersister,
    ILogger<QuestProgressionService> logger,
    LibreKO.Quests.Localization.IQuestTranslations? translations = null,
    TimeProvider? clock = null) : IQuestProgressionService
{
    public async Task HandleQuestAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch ((QuestSubOpcode)subOpcode)
        {
            case QuestSubOpcode.QuestList:
                session.Quest.ViewZone = session.ZoneId;
                await SendQuestDataAsync(session);
                break;

            case QuestSubOpcode.NotificationReply:
                if (packet.RemainingBytes == 6 && session.Hp > 0)
                    await objectives.ReplyToNotificationAsync(session, packet.ReadInt(), packet.ReadShort());
                break;

            case QuestSubOpcode.TargetDetail:
                if (packet.RemainingBytes == 5 && session.Hp > 0)
                    await objectives.ShowObjectiveTargetAsync(session, packet.ReadInt(), packet.ReadByte());
                break;

            case QuestSubOpcode.StateChange:
                break;

            case QuestSubOpcode.Accept:
                await ExecuteQuestEntryAsync(session, packet, QuestProgram.AcceptEvent);
                break;

            case QuestSubOpcode.CheckFulfill:
                await ExecuteQuestEntryAsync(session, packet, QuestProgram.FulfilEvent);
                break;

            case QuestSubOpcode.Abandon:
                await ExecuteQuestEntryAsync(session, packet, QuestProgram.AbandonEvent);
                break;

            case QuestSubOpcode.KillCounts:
                await HandleMonsterDataAsync(session, packet);
                break;

            default:
                logger.LogDebug("Unhandled quest sub-opcode {SubOp} from {Name}", subOpcode, session.Name);
                break;
        }
    }

    public async Task CheckQuestKillAsync(UserSession killer, int npcId)
    {
        var snapshot = killer.WithLock(s => s.Quest.QuestMap.ToArray());
        var killedName = gameDataService.GetNpc(npcId)?.Name;
        foreach (var (questId, state) in snapshot)
        {
            if (state != 1)
                continue;

            if (!TryGetKillObjectives(questId, out var groups, out var counts))
                continue;

            var zones = KillZones(questId);
            var step = killer.WithLock(s =>
            {
                var killCounts = s.Quest.GetOrCreateQuestKillCounts(questId);
                for (var groupIndex = 0; groupIndex < 4; groupIndex++)
                {
                    if (counts[groupIndex] <= 0 || killCounts[groupIndex] >= counts[groupIndex])
                        continue;
                    if (zones[groupIndex] > 0 && s.ZoneId != zones[groupIndex])
                        continue;

                    foreach (var monsterId in groups[groupIndex])
                    {
                        if (monsterId <= 0)
                            continue;
                        var matches = monsterId == npcId
                            || (!objectives.HasAutomaticFlow(questId) && !string.IsNullOrEmpty(killedName)
                                && string.Equals(gameDataService.GetNpc(monsterId)?.Name, killedName, StringComparison.Ordinal));
                        if (!matches)
                            continue;

                        killCounts[groupIndex]++;
                        if (s.Quest.ActiveQuestId == questId)
                            s.Quest.SyncActiveQuestKillCounts();

                        var completed = AreKillObjectivesComplete(
                            counts, killCounts, objectives.ObjectiveRuleFor(questId));
                        if (completed)
                            s.Quest.QuestMap[questId] = 3;

                        return (Incremented: true, GroupIndex: groupIndex, Count: killCounts[groupIndex], Completed: completed);
                    }
                }
                return (Incremented: false, GroupIndex: 0, Count: (ushort)0, Completed: false);
            });

            if (!step.Incremented)
                continue;

            await SendMonsterKillCountUpdateAsync(killer, questId, step.GroupIndex, step.Count);
            if (step.Completed)
            {
                await SendQuestStateUpdateAsync(killer, questId, QuestStatus.ReadyToTurnIn);
                PersistQuestState(killer);
            }
            await objectives.SendViewsAsync(killer, questId);
        }
    }

    private void PersistQuestState(UserSession session)
    {
        // The persister coalesces and rate-limits quest saves internally, so this is a
        // cheap, non-blocking request even when called rapidly for the same player.
        _ = statePersister.SaveQuestStateAsync(session);
    }

    private async Task ExecuteQuestEntryAsync(UserSession session, Packet packet, string role)
    {
        if (packet.RemainingBytes is not (4 or 5) || session.Hp <= 0)
            return;

        var requested = packet.ReadInt();
        if (requested is <= 0 or > short.MaxValue)
            return;
        var questId = (short)requested;
        var chosenReward = packet.RemainingBytes == 1 ? (sbyte)packet.ReadByte() : (sbyte)-1;
        RefreshDailyQuest(session, questId);
        if (role == QuestProgram.AbandonEvent && objectives.IsAutoAccepted(questId)) return;
        var state = (QuestStatus)session.Quest.QuestMap.GetValueOrDefault(questId);
        if (role == QuestProgram.AcceptEvent
            ? state is not (QuestStatus.NotStarted or QuestStatus.Abandoned)
            : state is not (QuestStatus.Active or QuestStatus.ReadyToTurnIn))
            return;

        var npc = session.Quest.EventNpcUniqueId > 0
            ? sessionManager.Regions.GetNpc(session.Quest.EventNpcUniqueId)
            : null;

        if (npc is not null && (!npc.IsAlive || npc.ZoneId != session.ZoneId
            || (session.X - npc.X) * (session.X - npc.X)
                + (session.Z - npc.Z) * (session.Z - npc.Z) > GameConstants.MaxNpcInteractionRangeSq))
            npc = null;
        if (role != QuestProgram.AbandonEvent && npc is null)
            return;

        if (role == QuestProgram.FulfilEvent
            && TryGetKillObjectives(questId, out _, out var required)
            && !AreKillObjectivesComplete(required, session.Quest.GetQuestKillCounts(questId),
                objectives.ObjectiveRuleFor(questId)))
            return;

        if (await dialogRunner.TryEntryAsync(session, npc, role, questId,
                role == QuestProgram.FulfilEvent ? chosenReward : (sbyte)-1))
            return;

        if (role == QuestProgram.AbandonEvent)
            await AbandonAsync(session, questId);
    }

    private async Task AbandonAsync(UserSession session, short questId)
    {
        session.Quest.QuestMap[questId] = 4;
        session.Quest.RemoveQuestKillCounts(questId);
        if (session.Quest.ActiveQuestId == questId)
        {
            session.Quest.ActiveQuestId = 0;
            session.Quest.SyncActiveQuestKillCounts();
        }

        await SendQuestStateUpdateAsync(session, questId, QuestStatus.Abandoned);
        await objectives.SendViewsAsync(session, questId);
        PersistQuestState(session);
    }

    private async Task HandleMonsterDataAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var requestType = packet.ReadByte();
        if (requestType == 1 && packet.RemainingBytes >= 2)
        {
            await SendMonsterKillCountsAsync(session, packet.ReadShort());
            return;
        }

        if (requestType == 2)
            return;
    }

    private async Task SendQuestDataAsync(UserSession session)
    {
        foreach (var questId in session.Quest.QuestMap.Keys.ToArray())
            RefreshDailyQuest(session, questId);
        // The client uses this to display in-game time on the quest panel.
        await session.Client.SendPacket(QuestPacketWriter.Clock(DateTime.UtcNow));

        var quests = session.Quest.QuestMap
            .Where(entry => objectives.TextFor(entry.Key) is not null || objectives.ObjectivesFor(entry.Key) is not null)
            .Select(entry => new QuestPacketWriter.QuestEntry(entry.Key, (QuestStatus)entry.Value))
            .ToList();
        await session.Client.SendPacket(QuestPacketWriter.QuestList(quests));
        await SendQuestTextsAsync(session, quests.Select(quest => (int)quest.QuestId));

        foreach (var quest in quests)
            if (objectives.ObjectivesFor(quest.QuestId) is { } declared)
                await session.Client.SendPacket(QuestPacketWriter.Objectives(declared));

        var activeQuestIds = quests
            .Where(quest => quest.Status is QuestStatus.Active or QuestStatus.ReadyToTurnIn)
            .Select(quest => quest.QuestId)
            .ToList();

        // Sub 9 — per-quest monster kill counts for every active / ready-to-turn-in quest.
        // Without this the quest panel reads 0/N for kill counts on login even though
        foreach (var questId in activeQuestIds)
        {
            await SendMonsterKillCountsAsync(session, questId, sendObjectives: false);
        }
        await objectives.SendViewsAsync(session);
    }

    private void RefreshDailyQuest(UserSession session, short questId)
    {
        var today = DateOnly.FromDateTime((clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        if (objectives.TextFor(questId)?.Daily == true && session.WithLock(s => s.Quest.RefreshDailyQuest(questId, today)))
            PersistQuestState(session);
    }

    private async Task SendQuestTextsAsync(UserSession session, IEnumerable<int> questIds)
    {
        var named = new List<QuestPacketWriter.TextEntry>();
        foreach (var questId in questIds.Distinct())
        {
            if (objectives.TextFor(questId, (int)session.Nation, ClassIdHelper.GroupOf(session.Class)) is not { } text)
                continue;
            named.Add(new QuestPacketWriter.TextEntry(
                (short)questId,
                Localize(session, text.Title),
                Localize(session, text.Journal)));
        }

        if (named.Count > 0)
            await session.Client.SendPacket(QuestPacketWriter.Texts(named));
    }

    private string Localize(UserSession session, string? text) =>
        text is null or { Length: 0 } ? string.Empty
            : translations?.Translate(session.LanguageCode, text) ?? text;

    private async Task SendMonsterKillCountsAsync(UserSession session, short eventDataIndex, bool sendObjectives = true)
    {
        if (!TryGetKillObjectives(eventDataIndex, out _, out _))
            return;

        if (sendObjectives && objectives.ObjectivesFor(eventDataIndex) is { } declared)
            await session.Client.SendPacket(QuestPacketWriter.Objectives(declared));

        var packet = QuestPacketWriter.KillCounts(
            eventDataIndex, session.Quest.GetQuestKillCounts(eventDataIndex));
        await session.Client.SendPacket(packet);
    }

    internal bool TryGetKillObjectives(short questId, out short[][] groups, out short[] counts)
    {
        if (objectives.ObjectivesFor(questId) is { Groups.Count: > 0 } declared)
        {
            groups = new short[4][];
            counts = new short[4];
            for (var index = 0; index < 4; index++)
            {
                var group = index < declared.Groups.Count ? declared.Groups[index] : null;
                groups[index] = group is null ? [] : [.. group.Monsters.Select(id => (short)id)];
                counts[index] = (short)(group?.Count ?? 0);
            }
            return true;
        }

        groups = [];
        counts = [];
        return false;
    }

    private int[] KillZones(short questId)
    {
        var zones = new int[4];
        var declared = objectives.ObjectivesFor(questId);
        for (var index = 0; declared is not null && index < Math.Min(4, declared.Groups.Count); index++)
            zones[index] = declared.Groups[index].Zone;
        return zones;
    }

    internal static bool AreKillObjectivesComplete(
        short[] requiredCounts,
        ushort[] currentCounts,
        ObjectiveRule rule = ObjectiveRule.All)
    {
        var wanted = 0;
        var met = 0;
        for (var index = 0; index < 4; index++)
        {
            if (requiredCounts[index] <= 0)
                continue;
            wanted++;
            if (currentCounts[index] >= requiredCounts[index])
                met++;
        }

        if (wanted == 0)
            return true;
        return rule == ObjectiveRule.Any ? met > 0 : met == wanted;
    }

    private static async Task SendQuestStateUpdateAsync(UserSession session, short eventDataIndex, QuestStatus status)
    {
        var packet = QuestPacketWriter.StateChange(eventDataIndex, status);
        await session.Client.SendPacket(packet);
    }

    private static async Task SendMonsterKillCountUpdateAsync(UserSession session, short eventDataIndex, int groupIndex, ushort count)
    {
        var packet = QuestPacketWriter.KillCountUpdate(eventDataIndex, groupIndex, count);
        await session.Client.SendPacket(packet);
    }
}
