using LibreKO.Quests.Runtime;
using LibreKO.Game.World;

namespace LibreKO.Game.Scripting;

public interface IQuestDefinitionSource
{
    QuestObjectives? ObjectivesFor(int questId);

    ObjectiveRule ObjectiveRuleFor(int questId);

    QuestText? TextFor(int questId, int nation = 0, int classGroup = 0);
    bool IsAutoAccepted(int questId) => false;
    bool HasAutomaticFlow(int questId) => false;
    Task SendViewsAsync(UserSession session, int? questId = null, bool changesOnly = false) => Task.CompletedTask;
    Task ReplyToNotificationAsync(UserSession session, int questId, int choice) => Task.CompletedTask;
    Task ShowObjectiveTargetAsync(UserSession session, int questId, int group) => Task.CompletedTask;
}
