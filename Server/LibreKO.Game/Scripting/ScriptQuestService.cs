using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using LibreKO.Game.Protocol.Writers;

#pragma warning disable IDE0060
using LibreKO.Common.Enums;

namespace LibreKO.Game.Scripting;

public class ScriptQuestService(
    UserSession session,
    List<Packet> queuedPackets,
    QuestScriptContext context)
{
    public void SetQuestState(int questId, byte status, bool hasKillObjectives)
    {
        if (questId <= 0 || questId > short.MaxValue)
            return;

        if (context.ActionFailed)
            return;

        var quest = (short)questId;
        session.WithLock(s =>
        {
            s.Quest.ActiveQuestId = quest;

            var previous = s.Quest.QuestMap.GetValueOrDefault(quest);
            s.Quest.QuestMap[quest] = status;
            if (status == 1 && previous is not (1 or 3) && hasKillObjectives)
                s.Quest.ResetQuestKillCounts(quest);
            else if (status is 0 or 2 or 4)
                s.Quest.RemoveQuestKillCounts(quest);
            s.Quest.SyncActiveQuestKillCounts();
            context.QuestStateDirty = true;
        });

        queuedPackets.Add(QuestPacketWriter.StateChange(quest, (QuestStatus)status));

        if (status == 1 && hasKillObjectives)
        {
            var counts = session.WithLock(s => s.Quest.GetQuestKillCounts(quest));
            queuedPackets.Add(QuestPacketWriter.KillCounts(quest, counts));
        }
    }

    public int GetQuestStatus(int _uid, int questId)
    {
        if (questId < short.MinValue || questId > short.MaxValue)
            return 0;

        return session.Quest.QuestMap.TryGetValue((short)questId, out var state)
            ? state
            : 0;
    }

    public bool CheckQuestEvent(int _uid, int questId, int state)
    {
        if (questId < short.MinValue || questId > short.MaxValue)
            return state == 0;

        return session.Quest.QuestMap.TryGetValue((short)questId, out var current)
            ? current == state
            : state == 0;
    }

    // groupIndex is 1-based (group 1 = KillCounts[0]).
    // When groupIndex is provided, return that group's count; otherwise sum all groups.
    public int CountMonsterQuestSub(int _uid, int _questId = 0, int groupIndex = 0)
    {
        var questId = _questId != 0 ? (short)_questId : session.Quest.ActiveQuestId;
        var counts = questId > 0 ? session.Quest.GetQuestKillCounts(questId) : session.Quest.KillCounts;

        if (groupIndex >= 1 && groupIndex <= 4)
            return counts[groupIndex - 1];

        var total = 0;
        for (var index = 0; index < 4; index++)
            total += counts[index];
        return total;
    }

    public int CountMonsterQuestMain(int _uid) => CountMonsterQuestSub(_uid);

    public int ExistMonsterQuestSub(int _uid) => 0;
}
#pragma warning restore IDE0060
