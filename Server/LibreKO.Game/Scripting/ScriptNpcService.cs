using LibreKO.Game.World;

using LibreKO.Common.Enums;

namespace LibreKO.Game.Scripting;

public class ScriptNpcService(NpcInstance? npc, QuestScriptContext? context = null)
{
    public bool SendNpcKillID(int _uid, int _npcId)
    {
        context?.RequestNpcDespawn();
        return npc is not null;
    }

    public int GetNpcID() => npc?.UniqueId ?? 0;
    public int GetNpcProtoID() => npc?.NpcId ?? 0;
    public int GetNpcNation() => (int)(npc?.Nation ?? EntityNation.All);

    public string NpcGetName() => npc?.Name ?? string.Empty;
    public int NpcGetID() => npc?.UniqueId ?? 0;
    public int NpcGetProtoID() => npc?.NpcId ?? 0;
    public int NpcGetType() => npc?.NpcType ?? 0;
    public int NpcGetNation() => (int)(npc?.Nation ?? EntityNation.All);
    public int NpcGetZoneID() => npc?.ZoneId ?? 0;
    public double NpcGetX() => npc?.X ?? 0;
    public double NpcGetY() => npc?.Y ?? 0;
    public double NpcGetZ() => npc?.Z ?? 0;
}
