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
    IKingSystemRuntimeService kingSystemRuntimeService,
    IDrakiTowerService drakiTowerService,
    IDrakiStageProvider drakiStageProvider,
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

        if (scriptFile is DrakiRiftScript or DrakiGateScript or DrakiExitScript)
        {
            Array.Fill(session.Quest.SelectMessageEvents, -1);
            Array.Fill(session.Quest.SelectMessageRewards, -1);
            session.Quest.IsScriptDialog = false;
            session.Quest.ActiveQuestScript = string.Empty;

            if (eventId == DrakiActionEnter)
            {
                var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
                enterPacket.WriteInt(0);
                enterPacket.WriteByte(1);
                await drakiTowerService.HandleEnterAsync(session, enterPacket);
            }
            else if (eventId == DrakiActionAdvance && npc is not null)
            {
                await drakiTowerService.AdvanceFromGateNpcAsync(session, npc);
            }
            else if (eventId == DrakiActionExit)
            {
                await drakiTowerService.HandleTownAsync(session);
            }
            return;
        }

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

        var npcData = gameDataService.GetNpc(npc.NpcId, !npc.UsesNpcSpawnStyle);
        if (DrakiTowerRules.IsDrakiNpc(npc.NpcId) && npcData != null && await TryHandleNpcUiAsync(session, npc, npcData))
            return;

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

        if (await TryHandleNpcUiAsync(session, npc, npcData))
            return;

        if (!await dialogRunner.TryGreetAsync(session, npc))
            logger.LogDebug("No quest script greets player {Name} at NPC {NpcId}",
                session.Name, npc.NpcId);
    }

    private const string DrakiRiftScript = "draki_rift";
    private const string DrakiGateScript = "draki_gate";
    private const string DrakiExitScript = "draki_exit";

    public const int DrakiActionEnter = 1;
    public const int DrakiActionRanking = 2;
    public const int DrakiActionAdvance = 3;
    public const int DrakiActionExit = 4;

    private async Task<bool> TryHandleNpcUiAsync(UserSession session, NpcInstance npc, NpcData npcData)
    {
        if (npc.NpcId == DrakiTowerRules.DrakiRiftNpcId)
        {
            await ShowDrakiRiftDialogAsync(session, npc);
            return true;
        }

        if (session.ZoneId == DrakiTowerRules.ZoneIdValue && DrakiTowerRules.IsGateNpc(npc.NpcId))
        {
            await ShowDrakiGateDialogAsync(session, npc);
            return true;
        }

        if (session.ZoneId == DrakiTowerRules.ZoneIdValue && npc.NpcId == DrakiTowerRules.FinalExitNpcId)
        {
            await ShowDrakiExitDialogAsync(session, npc);
            return true;
        }

        Packet? response = npc.NpcId == NpcData.MakeupArtist ? PreGamePacketWriter.ChangeHairShop() : npcData.NpcType switch
        {
            NpcData.TypeTradeMerchant => BuildTradeNpcPacket(npcData),
            NpcData.TypeRepairMerchant => BuildRepairNpcPacket(npcData),
            NpcData.TypeAnvil => BuildAnvilPacket(npc),
            NpcData.TypeClanCape => BuildClanCapePacket(),
            NpcData.TypeWarehouse => BuildWarehousePacket(),
            NpcData.TypeClassChange => BuildClassChangePacket(),
            NpcData.TypeChaoticGenerator => BuildChaoticGeneratorPacket(npc),
            NpcData.TypeRental => RentalPacketWriter.NpcState(RentalPacketWriter.Unavailable, npcData.SellingGroup),
            NpcData.TypeElectionOfficer => BuildElectionOfficerPacket(session),
            NpcData.TypeGrandChamberlain => BuildTreasuryPacket(session),
            NpcData.TypeSiegeWarfare => SiegePacketWriter.WarfareNpc(),
            NpcData.TypeCastleManager => BuildCastleManagerPacket(session),
            _ => null
        };

        if (response == null)
            return false;

        await session.Client.SendPacket(response);
        return true;
    }

    private Packet BuildElectionOfficerPacket(UserSession session) =>
        KingPacketWriter.ElectionOfficer(kingSystemRuntimeService.GetKingData(session.Nation)?.KingName?.Trim() ?? string.Empty);

    private Packet BuildTreasuryPacket(UserSession session)
    {
        var kingData = kingSystemRuntimeService.GetKingData(session.Nation);
        var treasury = (uint)Math.Max(kingData?.NationalTreasury ?? 0, 0);
        if (kingData == null || !kingSystemRuntimeService.IsKing(session, kingData))
            return KingPacketWriter.CitizenTreasury(treasury);

        var kingsFund = (long)Math.Max(kingData.Tribute, 0) + Math.Max(kingData.TerritoryTax, 0);
        return KingPacketWriter.KingTreasury((uint)Math.Min(kingsFund, uint.MaxValue), treasury);
    }

    private Packet? BuildCastleManagerPacket(UserSession session)
    {
        var siege = gameDataService.SiegeWarfare;
        if (siege == null || !SiegeRules.IsCastleLord(session, siege))
            return null;

        return SiegePacketWriter.CastleManager(
            (uint)Math.Max(siege.DungeonCharge, 0), (uint)Math.Max(siege.MoradonTax, 0));
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

    internal static bool IsInNpcRange(UserSession session, NpcInstance npc)
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

    private async Task ShowDrakiRiftDialogAsync(UserSession session, NpcInstance npc)
    {
        DrakiTowerRules.EnsureDailyLimit(session);

        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = DrakiRiftScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        session.Quest.SelectMessageEvents[0] = DrakiActionEnter;

        var header = $"[Draki's Tower]\n\nWelcome, warrior. Through this dimensional rift lies Draki's Tower.\nConquer each stage to earn great experience and rewards!\n\nToday's Remaining Entries: {session.DrakiEntranceLimit}/{DrakiTowerRules.MaxDailyEntrances}\nRequired Level: {DrakiTowerRules.MinimumLevel}+";
        var buttonTexts = new[]
        {
            "Enter Draki's Tower"
        };

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            -1,
            [-1, -1],
            UserSession.SelectMessageEventCount,
            DrakiRiftScript,
            header,
            buttonTexts);

        await session.Client.SendPacket(dialogPacket);
        await drakiTowerService.HandleListAsync(session);
    }

    private async Task ShowDrakiGateDialogAsync(UserSession session, NpcInstance npc)
    {
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = DrakiGateScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        session.Quest.SelectMessageEvents[0] = DrakiActionAdvance;

        var npcData = gameDataService.GetNpc(npc.NpcId, isMonster: false);
        var npcName = string.IsNullOrWhiteSpace(npcData?.Name) ? "Gate Keeper" : npcData.Name;

        var state = drakiTowerService.GetRoomState((ushort)session.Room);
        var stages = drakiStageProvider.Stages;
        var nextIndex = state != null ? state.StageIndex + 1 : -1;
        var nextStage = nextIndex >= 0 && nextIndex < stages.Count ? stages[nextIndex] : null;

        string header;
        string advanceButton;

        if (nextStage != null && nextStage.Stage == session.DrakiStage)
        {
            header = $"[{npcName}]\n\nThank you for saving me and defeating the monsters!\nThe path ahead leads to Wave {nextStage.SubStage} of Floor {nextStage.Stage}.\nAre you ready to continue?";
            advanceButton = $"Advance to Wave {nextStage.SubStage}";
        }
        else
        {
            var nextFloor = nextStage?.Stage ?? (session.DrakiStage + 1);
            header = $"[{npcName}]\n\nThank you for saving me and defeating the monsters!\nThe gate behind me leads to Floor {nextFloor}.\nAre you ready to advance to the next floor?";
            advanceButton = $"Advance to Floor {nextFloor}";
        }

        var buttonTexts = new[]
        {
            advanceButton,
            "Stay here to prepare / use Sundries"
        };

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            -1,
            [-1, -1],
            UserSession.SelectMessageEventCount,
            DrakiGateScript,
            header,
            buttonTexts);

        await session.Client.SendPacket(dialogPacket);
    }

    private async Task ShowDrakiExitDialogAsync(UserSession session, NpcInstance npc)
    {
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = DrakiExitScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        session.Quest.SelectMessageEvents[0] = DrakiActionExit;

        var header = "[Draki Rift Exit]\n\nYou have vanquished the final boss and conquered Draki's Tower!\nStep through this rift to safely return to your castle.";
        var buttonTexts = new[]
        {
            "Exit Draki's Tower",
            "Stay here a bit longer"
        };

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            -1,
            [-1, -1],
            UserSession.SelectMessageEventCount,
            DrakiExitScript,
            header,
            buttonTexts);

        await session.Client.SendPacket(dialogPacket);
    }
}


