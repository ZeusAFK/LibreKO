using LibreKO.Game.World;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Scripting;

public interface IQuestDialogRunner
{
    Task<bool> RunAsync(UserSession session, NpcInstance? npc, int eventId, sbyte selectedReward, string scriptName);

    Task<bool> TryGreetAsync(UserSession session, NpcInstance npc);

    Task<bool> TryEntryAsync(UserSession session, NpcInstance? npc, string role, int questId,
        sbyte chosenReward = -1);
}

public sealed class QuestDialogRunner(
    QuestScriptEngine questScripts,
    ILogger<QuestDialogRunner> logger) : IQuestDialogRunner
{
    public async Task<bool> TryGreetAsync(UserSession session, NpcInstance npc)
    {
        if (!questScripts.TryGetGreeting(npc.NpcId, session.ZoneId, out var scriptName, out var eventId))
            return false;

        logger.LogDebug(
            "Greeting {Name} at NPC {NpcId} from {Script} event {Event}",
            session.Name, npc.NpcId, scriptName, eventId);
        return await questScripts.ExecuteAsync(session, npc, eventId, -1, scriptName);
    }

    public async Task<bool> TryEntryAsync(
        UserSession session,
        NpcInstance? npc,
        string role,
        int questId,
        sbyte chosenReward = -1)
    {
        if (role == QuestProgram.AbandonEvent)
        {
            if (!questScripts.TryGetAbandonEntry(questId, out var abandonScript, out var abandonEvent))
                return false;
            if (abandonScript is not null)
            {
                Array.Fill(session.Quest.SelectMessageEvents, -1);
                session.Quest.EventNpcId = 0;
                session.Quest.EventNpcUniqueId = 0;
                await questScripts.ExecuteAsync(session, null, abandonEvent, -1, abandonScript);
            }
            return true;
        }
        if (!questScripts.TryGetEntry(npc?.NpcId ?? 0, session.ZoneId, role, questId,
                out var scriptName, out var eventId))
            return false;

        logger.LogDebug(
            "Entry \"{Role}\" for quest {Quest} from {Script} event {Event} for {Name}",
            role, questId, scriptName, eventId, session.Name);
        return await questScripts.ExecuteAsync(session, npc, eventId, chosenReward, scriptName);
    }

    public async Task<bool> RunAsync(
        UserSession session,
        NpcInstance? npc,
        int eventId,
        sbyte selectedReward,
        string scriptName)
    {
        if (questScripts.Handles(scriptName))
        {
            if (await questScripts.ExecuteAsync(session, npc, eventId, selectedReward, scriptName))
                return true;

            logger.LogDebug(
                "Quest script for {Script} did not handle event {Event}",
                scriptName, eventId);
        }

        return false;
    }
}
