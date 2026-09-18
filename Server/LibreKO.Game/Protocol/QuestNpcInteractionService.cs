using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Quests.Runtime;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

using LibreKO.Common.Enums;

namespace LibreKO.Game.Protocol;

public interface IQuestNpcInteractionService
{
    Task HandleSelectMsgAsync(IClient client, Packet packet);
    Task HandleClientEventAsync(IClient client, Packet packet);
    Task HandleNpcEventAsync(IClient client, Packet packet);
}

public class QuestNpcInteractionService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IQuestDialogRunner dialogRunner,
    ILogger<QuestNpcInteractionService> logger) : IQuestNpcInteractionService
{
    private const byte WarehouseRequest = 0x10;

    public async Task HandleSelectMsgAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var payloadHex = Convert.ToHexString(packet.GetData());
        var menuIndex = packet.ReadByte();
        if (menuIndex >= session.Quest.SelectMessageEvents.Length)
        {
            logger.LogInformation(
                "SELECT_MSG ignored for player {Name}: menuIndex {MenuIndex} out of range, events [{Events}], payload {Payload}",
                session.Name,
                menuIndex,
                string.Join(", ", session.Quest.SelectMessageEvents),
                payloadHex);
            return;
        }

        _ = packet.RemainingBytes > 0
            ? packet.ReadSByteString().Trim()
            : string.Empty;
        var scriptFile = session.Quest.ActiveQuestScript;

        sbyte selectedReward = -1;
        if (packet.RemainingBytes >= 1)
            selectedReward = (sbyte)packet.ReadByte();

        var effectiveMenuIndex = menuIndex;
        if (session.Quest.IsScriptDialog)
            selectedReward = (sbyte)session.Quest.SelectMessageRewards[menuIndex];

        var eventId = session.Quest.SelectMessageEvents[effectiveMenuIndex];
        if (eventId < 0)
        {
            logger.LogInformation(
                "SELECT_MSG ignored for player {Name}: menuIndex {MenuIndex} resolved to empty event, events [{Events}], payload {Payload}",
                session.Name,
                effectiveMenuIndex,
                string.Join(", ", session.Quest.SelectMessageEvents),
                payloadHex);
            Array.Fill(session.Quest.SelectMessageEvents, -1);
            return;
        }

        if (string.IsNullOrEmpty(scriptFile))
        {
            logger.LogDebug("SELECT_MSG: No quest script for player {Name} event {EventId}", session.Name, eventId);
            return;
        }

        logger.LogInformation(
            "SELECT_MSG click for player {Name}: npc {NpcId}, menuIndex {MenuIndex}, eventId {EventId}, scriptFile {ScriptFile}, selectedReward {SelectedReward}, events [{Events}], payload {Payload}",
            session.Name,
            session.Quest.EventNpcId,
            menuIndex,
            eventId,
            scriptFile,
            selectedReward,
            string.Join(", ", session.Quest.SelectMessageEvents),
            payloadHex);

        var npc = session.Quest.EventNpcUniqueId > 0
            ? sessionManager.Regions.GetNpc(session.Quest.EventNpcUniqueId)
            : null;

        if (IsBusy(session) || (session.Quest.EventNpcUniqueId > 0
            && (npc is null || !npc.IsAlive || npc.ZoneId != session.ZoneId || !IsInNpcRange(session, npc))))
            return;

        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);
        session.Quest.IsScriptDialog = false;
        var executed = await dialogRunner.RunAsync(session, npc, eventId, selectedReward, scriptFile);

        logger.LogInformation(
            "SELECT_MSG result for player {Name}: executed {Executed}, class {Class}, money {Money}, activeQuestId {QuestId}, questState {QuestState}, activeScript {ActiveScript}",
            session.Name,
            executed,
            session.Class,
            session.Money,
            session.Quest.ActiveQuestId,
            session.Quest.ActiveQuestId > 0 && session.Quest.QuestMap.TryGetValue(session.Quest.ActiveQuestId, out var state) ? state : (byte)0,
            session.Quest.ActiveQuestScript);
    }

    public async Task HandleClientEventAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 4)
            return;

        var npcUniqueId = packet.ReadInt();

        // npcUniqueId == 0 is a zone auto-event trigger (helpers with NpcId=0, e.g. intro quests)
        if (npcUniqueId == 0)
        {
            await TryRunZoneAutoEventAsync(session);
            return;
        }

        var npc = sessionManager.Regions.GetNpc(npcUniqueId);
        if (npc == null || !npc.IsAlive || npc.ZoneId != session.ZoneId || !IsInNpcRange(session, npc))
            return;

        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npcUniqueId;
        ResetDialog(session);

        if (!await dialogRunner.TryGreetAsync(session, npc))
            logger.LogDebug("No quest script greets player {Name} at NPC {NpcId} (client event)",
                session.Name, npc.NpcId);
    }

    private async Task TryRunZoneAutoEventAsync(UserSession session)
    {
        session.Quest.EventNpcId = 0;
        session.Quest.EventNpcUniqueId = 0;
        ResetDialog(session);

        await dialogRunner.TryEntryAsync(session, null, QuestProgram.ZoneEntryEvent, 0);
    }

    public async Task HandleNpcEventAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 5 || IsBusy(session))
            return;

        packet.ReadByte();
        var npcUniqueId = packet.ReadInt();

        if (packet.RemainingBytes >= 4)
            _ = packet.ReadInt();

        var npc = sessionManager.Regions.GetNpc(npcUniqueId);
        if (npc == null || !npc.IsAlive || npc.ZoneId != session.ZoneId || !IsInNpcRange(session, npc))
            return;

        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npcUniqueId;
        ResetDialog(session);

        var npcData = gameDataService.GetNpc(npc.NpcId, !npc.UsesNpcSpawnStyle);
        if (npcData == null)
            return;

        if (await TryHandleNpcUiAsync(client, npc, npcData))
            return;

        if (!await dialogRunner.TryGreetAsync(session, npc))
            logger.LogDebug("No quest script greets player {Name} at NPC {NpcId}",
                session.Name, npc.NpcId);
    }

    private static async Task<bool> TryHandleNpcUiAsync(IClient client, NpcInstance npc, NpcData npcData)
    {
        Packet? response = npcData.NpcType switch
        {
            NpcData.TypeTradeMerchant => BuildTradeNpcPacket(npcData),
            NpcData.TypeRepairMerchant => BuildRepairNpcPacket(npcData),
            NpcData.TypeAnvil => BuildAnvilPacket(npc),
            NpcData.TypeClanCape => BuildClanCapePacket(),
            NpcData.TypeWarehouse => BuildWarehousePacket(),
            NpcData.TypeClassChange => BuildClassChangePacket(),
            NpcData.TypeMakeupArtist => PreGamePacketWriter.ChangeHairShop(),
            NpcData.TypeChaoticGenerator => BuildChaoticGeneratorPacket(npc),
            _ => null
        };

        if (response == null)
            return false;

        await client.SendPacket(response);
        return true;
    }

    private static bool IsBusy(UserSession session) =>
        session.Hp <= 0
        || session.Trade.IsTrading
        || session.Trade.IsMerchanting
        || session.IsGathering;

    private static void ResetDialog(UserSession session)
    {
        session.Quest.ActiveQuestScript = string.Empty;
        session.Quest.IsScriptDialog = false;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);
    }

    private static bool IsInNpcRange(UserSession session, NpcInstance npc)
    {
        var dx = session.X - npc.X;
        var dz = session.Z - npc.Z;
        return dx * dx + dz * dz <= GameConstants.MaxNpcInteractionRangeSq;
    }

    private static Packet BuildTradeNpcPacket(NpcData npcData) =>
        NpcServicePacketWriter.TradeNpc(npcData.SellingGroup);

    private static Packet BuildRepairNpcPacket(NpcData npcData) =>
        NpcServicePacketWriter.RepairNpc(npcData.SellingGroup);

    private static Packet BuildAnvilPacket(NpcInstance npc)
    {
        return ItemUpgradePacketWriter.AnvilOpen(npc.UniqueId);
    }
    private static Packet BuildChaoticGeneratorPacket(NpcInstance npc)
    {
        return ItemUpgradePacketWriter.BifrostRequest(npc.UniqueId);
    }

    private static Packet BuildClanCapePacket() => NpcServicePacketWriter.ClanCapeNpc();

    private static Packet BuildWarehousePacket() =>
        NpcServicePacketWriter.WarehouseNpc(WarehouseRequest);

    private static Packet BuildClassChangePacket() => NpcServicePacketWriter.ClassChangeNpc();
}
