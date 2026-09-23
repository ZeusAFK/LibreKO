using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Scripting;

public class QuestScriptContext
{
    public List<Packet> QueuedPackets { get; } = [];

    public bool QuestStateDirty { get; set; }

    public bool RunClanNts { get; set; }
    public int PromotedClanId { get; set; }

    public int PremiumClanId { get; set; }

    public bool ActionFailed { get; private set; }

    public bool ClassChanged { get; set; }

    public List<int> PendingAchievements { get; } = [];

    public bool PendingTempleEventJoin { get; set; }

    public int PendingLevel { get; set; }

    public long PendingExperience { get; private set; }

    public void AddExperience(long amount) => PendingExperience += amount;

    public (int ZoneId, float X, float Z)? PendingZoneChange { get; private set; }

    public NpcInstance? EventNpc { get; }

    public bool DespawnEventNpc { get; private set; }

    public void RequestNpcDespawn() => DespawnEventNpc = true;

    public List<(int NpcId, int Count, int X, int Z)> PendingSummons { get; } = [];

    public void RequestSummon(int npcId, int count, int x, int z) => PendingSummons.Add((npcId, count, x, z));

    public void RequestZoneChange(int zoneId, float x, float z) => PendingZoneChange = (zoneId, x, z);

    public (int ZoneId, int Set, float X, float Z)? PendingInstance { get; private set; }

    public void RequestInstance(int zoneId, int set, float x, float z) => PendingInstance = (zoneId, set, x, z);

    public void FailAction(string reason)
    {
        ActionFailed = true;
        FailureReason ??= reason;
    }

    public string? FailureReason { get; private set; }

    public ScriptDialogService Dialog { get; }
    public ScriptPlayerQueryService Player { get; }
    public ScriptItemService Items { get; }
    public ScriptQuestService Quest { get; }
    public ScriptCharacterService Character { get; }
    public ScriptClanPartyService ClanParty { get; }
    public ScriptNpcService Npc { get; }

    public QuestScriptContext(
        UserSession session,
        NpcInstance? npc,
        IGameDataService gameData,
        SessionManager sessionManager,
        ILogger logger,
        int expMultiplier)
    {
        Dialog = new ScriptDialogService(session, npc, QueuedPackets, this);
        Player = new ScriptPlayerQueryService(session, npc, gameData, sessionManager, logger);
        Character = new ScriptCharacterService(session, npc, gameData, QueuedPackets, expMultiplier, this);
        Items = new ScriptItemService(session, gameData, QueuedPackets, logger, Character, this);
        Quest = new ScriptQuestService(session, QueuedPackets, this);
        ClanParty = new ScriptClanPartyService(session, sessionManager, Items, this);
        Npc = new ScriptNpcService(npc, this);
        EventNpc = npc;
    }
}
