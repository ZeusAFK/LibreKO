using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public static class NpcPresence
{
    public static bool IsAt(SessionManager sessions, UserSession session, Func<NpcInstance, bool> belongs) =>
        sessions.Regions.GetNpc(session.Quest.EventNpcUniqueId) is { IsAlive: true } npc
        && belongs(npc)
        && npc.ZoneId == session.ZoneId
        && QuestNpcInteractionService.IsInNpcRange(session, npc);

    public static bool IsAt(SessionManager sessions, UserSession session, int npcId) =>
        IsAt(sessions, session, npc => npc.NpcId == npcId);
}
