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
    ITouchTrapService touchTrapService,
    IChaosDungeonService chaosDungeonService,
    IBifrostEventService bifrostEventService,
    ILogger<QuestNpcInteractionService> logger,
    IFullMoonRiftService fullMoonRiftService,
    IItemGrantService itemGrantService,
    IMagicItemUsageService magicItemUsageService) : IQuestNpcInteractionService
{
    private const byte WarehouseRequest = 0x10;
    private const byte NpcEventTouch = 2;

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

        if (scriptFile == FullMoonBlancScript)
        {
            Array.Fill(session.Quest.SelectMessageEvents, -1);
            Array.Fill(session.Quest.SelectMessageRewards, -1);
            session.Quest.IsScriptDialog = false;
            session.Quest.ActiveQuestScript = string.Empty;

            await HandleBlancActionAsync(session, eventId, npc);
            return;
        }

        if (scriptFile == FullMoonPriestScript)
        {
            Array.Fill(session.Quest.SelectMessageEvents, -1);
            Array.Fill(session.Quest.SelectMessageRewards, -1);
            session.Quest.IsScriptDialog = false;
            session.Quest.ActiveQuestScript = string.Empty;

            await HandlePriestActionAsync(session, eventId);
            return;
        }

        if (scriptFile is DrakiRiftScript or DrakiGateScript or DrakiExitScript)
        {
            Array.Fill(session.Quest.SelectMessageEvents, -1);
            Array.Fill(session.Quest.SelectMessageRewards, -1);
            session.Quest.IsScriptDialog = false;
            session.Quest.ActiveQuestScript = string.Empty;

            if (eventId is >= DrakiActionEnterBase + UserSession.DrakiStageMin and <= DrakiActionEnterBase + UserSession.DrakiStageMax)
            {
                var floor = (byte)(eventId - DrakiActionEnterBase);
                var enterPacket = new Packet((byte)GameOpcodes.GS_EVENT);
                enterPacket.WriteInt(0);
                enterPacket.WriteByte(floor);
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
        if ((DrakiTowerRules.IsDrakiNpc(npc.NpcId) || npc.NpcId is FullMoonRules.PriestOfFullMoonNpcId or FullMoonRules.NexusTowerNpcId)
            && npcData != null && await TryHandleNpcUiAsync(session, npc, npcData))
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

        var kind = packet.ReadByte();
        var npcUniqueId = packet.ReadInt();

        if (packet.RemainingBytes >= 4)
            _ = packet.ReadInt();

        var npc = sessionManager.Regions.GetNpc(npcUniqueId);
        if (npc == null || !npc.IsAlive || npc.ZoneId != session.ZoneId || !IsInNpcRange(session, npc))
            return;

        if (await chaosDungeonService.TryTouchCubeAsync(session, npc))
            return;

        if (npc.NpcId == BifrostRules.GateNpcId && !npc.IsMonster && npc.ZoneId == BifrostRules.ZoneRonarkLand)
        {
            await bifrostEventService.TryEnterAsync(session);
            return;
        }

        if (kind == NpcEventTouch)
        {
            await touchTrapService.TryTouchAsync(session, npc);
            return;
        }

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

    public const int DrakiActionAdvance = 3;
    public const int DrakiActionExit = 4;
    public const int DrakiActionEnterBase = 10;

    private async Task<bool> TryHandleNpcUiAsync(UserSession session, NpcInstance npc, NpcData npcData)
    {
        if (npc.NpcId == FullMoonRules.BlancNpcId)
        {
            await ShowBlancDialogAsync(session, npc);
            return true;
        }

        if (npc.NpcId is FullMoonRules.PriestOfFullMoonNpcId or FullMoonRules.NexusTowerNpcId)
        {
            await ShowPriestDialogAsync(session, npc);
            return true;
        }

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

        var maxFloor = Math.Clamp((int)session.DrakiStage, (int)UserSession.DrakiStageMin, (int)UserSession.DrakiStageMax);
        var buttonTexts = new List<string>();
        for (byte f = UserSession.DrakiStageMin; f <= maxFloor; f++)
        {
            var idx = f - UserSession.DrakiStageMin;
            session.Quest.SelectMessageEvents[idx] = DrakiActionEnterBase + f;
            buttonTexts.Add($"Enter Floor {f}");
        }

        var header = $"[Draki's Tower]\n\nWelcome, warrior. Through this dimensional rift lies Draki's Tower.\nConquer each stage to earn great experience and rewards!\n\nToday's Remaining Entries: {session.DrakiEntranceLimit}/{DrakiTowerRules.MaxDailyEntrances}\nRequired Level: {DrakiTowerRules.MinimumLevel}+";

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
        var currentRunFloor = state?.CurrentStage ?? session.DrakiStage;

        string header;
        string advanceButton;

        if (nextStage != null && nextStage.Stage == currentRunFloor)
        {
            header = $"[{npcName}]\n\nThank you for saving me and defeating the monsters!\nThe path ahead leads to Wave {nextStage.SubStage} of Floor {nextStage.Stage}.\nAre you ready to continue?";
            advanceButton = $"Advance to Wave {nextStage.SubStage}";
        }
        else
        {
            var nextFloor = nextStage?.Stage ?? (currentRunFloor + 1);
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

    private const string FullMoonBlancScript = "fullmoon_blanc";

    private async Task ShowBlancDialogAsync(UserSession session, NpcInstance npc)
    {
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = FullMoonBlancScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        var buttons = new[]
        {
            FullMoonRules.BlancBtnEnterRift,
            FullMoonRules.BlancBtnOpenBoard,
            FullMoonRules.BlancBtnExchangeJewel,
            FullMoonRules.BlancBtnReceiveVoucher
        };

        for (int i = 0; i < buttons.Length; i++)
            session.Quest.SelectMessageEvents[i] = buttons[i];

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            FullMoonRules.BlancTalkId,
            buttons,
            UserSession.SelectMessageEventCount,
            FullMoonBlancScript);

        await session.Client.SendPacket(dialogPacket);
    }

    private async Task ShowExchangeSubmenuAsync(UserSession session, NpcInstance npc)
    {
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = FullMoonBlancScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        var buttons = new[]
        {
            FullMoonRules.ExchangeBtnOldDraki,
            FullMoonRules.ExchangeBtnDraki,
            FullMoonRules.ExchangeBtnGrace,
            FullMoonRules.ExchangeBtnTattoo7d,
            FullMoonRules.ExchangeBtnRabbit,
            FullMoonRules.ExchangeBtnTattoo30d
        };

        for (int i = 0; i < buttons.Length; i++)
            session.Quest.SelectMessageEvents[i] = buttons[i];

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            FullMoonRules.BlancTalkId,
            buttons,
            UserSession.SelectMessageEventCount,
            FullMoonBlancScript);

        await session.Client.SendPacket(dialogPacket);
    }

    private async Task HandleBlancActionAsync(UserSession session, int eventId, NpcInstance? npc)
    {
        switch (eventId)
        {
            case FullMoonRules.BlancBtnEnterRift:
                await fullMoonRiftService.HandlePartyEntryAsync(session);
                break;

            case FullMoonRules.BlancBtnOpenBoard:
                if (npc != null)
                {
                    var boardPacket = NpcDialogPacketWriter.SelectMessage(
                        npc.NpcId,
                        57,
                        NpcDialogPacketWriter.NoText,
                        NpcDialogPacketWriter.NoText,
                        [],
                        UserSession.SelectMessageEventCount,
                        FullMoonBlancScript);
                    await session.Client.SendPacket(boardPacket);
                }
                break;

            case FullMoonRules.BlancBtnExchangeJewel:
                if (npc != null)
                    await ShowExchangeSubmenuAsync(session, npc);
                break;

            case FullMoonRules.BlancBtnReceiveVoucher:
                await ClaimVoucherAsync(session);
                break;

            case FullMoonRules.ExchangeBtnOldDraki:
            case FullMoonRules.ExchangeBtnDraki:
            case FullMoonRules.ExchangeBtnGrace:
            case FullMoonRules.ExchangeBtnTattoo7d:
            case FullMoonRules.ExchangeBtnRabbit:
            case FullMoonRules.ExchangeBtnTattoo30d:
                await HandleExchangeAsync(session, eventId);
                break;
        }
    }

    private async Task HandleExchangeAsync(UserSession session, int buttonId)
    {
        var (itemId, cost) = buttonId switch
        {
            FullMoonRules.ExchangeBtnOldDraki => (FullMoonRules.OldDrakiSupplyBoxItem, FullMoonRules.OldDrakiSupplyBoxCost),
            FullMoonRules.ExchangeBtnDraki => (FullMoonRules.DrakiSupplyBoxItem, FullMoonRules.DrakiSupplyBoxCost),
            FullMoonRules.ExchangeBtnGrace => (FullMoonRules.GraceOfMoonGodItem, FullMoonRules.GraceOfMoonGodCost),
            FullMoonRules.ExchangeBtnTattoo7d => (FullMoonRules.LunarTattoo7DaysItem, FullMoonRules.LunarTattoo7DaysCost),
            FullMoonRules.ExchangeBtnRabbit => (FullMoonRules.RabbitTransformScrollItem, FullMoonRules.RabbitTransformScrollCost),
            FullMoonRules.ExchangeBtnTattoo30d => (FullMoonRules.LunarTattoo30DaysItem, FullMoonRules.LunarTattoo30DaysCost),
            _ => (0, 0)
        };

        if (itemId == 0)
            return;

        var jewelCount = session.Inventory.Where(s => s.ItemId == FullMoonRules.RiftJewelItem).Sum(s => (int)s.Count);
        if (jewelCount < cost)
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.PriestNotEnoughCoinsTalkId]));
            return;
        }

        var rewardItem = gameDataService.GetItem(itemId);
        if (rewardItem == null)
            return;

        var targetSlot = session.WithLock(s => s.FindSlotForItem(itemId, gameDataService, 1));
        if (targetSlot < 0)
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.PriestInventoryFullTalkId]));
            return;
        }

        if (!await magicItemUsageService.TryConsumeItemAsync(session, FullMoonRules.RiftJewelItem, cost))
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.PriestNotEnoughCoinsTalkId]));
            return;
        }

        await itemGrantService.GrantAsync(session, rewardItem, 1);
    }

    private async Task ClaimVoucherAsync(UserSession session)
    {
        if (FullMoonRules.HasEnteredToday(session))
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.AlreadyEnteredTodayTalkId]));
            return;
        }

        if (FullMoonRules.HasVoucher(session))
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.AlreadyHaveVoucherTalkId]));
            return;
        }

        var voucherItem = gameDataService.GetItem(FullMoonRules.VoucherItem);
        if (voucherItem == null)
            return;

        var targetSlot = session.WithLock(s => s.FindSlotForItem(FullMoonRules.VoucherItem, gameDataService, 1));
        if (targetSlot < 0)
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.PriestInventoryFullTalkId]));
            return;
        }

        var placed = await itemGrantService.GrantAsync(session, voucherItem, 1);
        if (placed > 0)
        {
            var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + FullMoonRules.VoucherDurationSeconds;
            session.WithLock(s =>
            {
                for (int i = 0; i < s.Inventory.Length; i++)
                {
                    if (s.Inventory[i].ItemId == FullMoonRules.VoucherItem && s.Inventory[i].ExpiresAt == 0)
                    {
                        s.Inventory[i].ExpiresAt = expiry;
                        break;
                    }
                }
                return true;
            });
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.VoucherClaimedTalkId]));
        }
    }

    private const string FullMoonPriestScript = "fullmoon_priest";

    private async Task ShowPriestDialogAsync(UserSession session, NpcInstance npc)
    {
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = FullMoonPriestScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        var buttons = new[]
        {
            FullMoonRules.PriestBtnDrainRestore,
            FullMoonRules.PriestBtnFireStrike,
            FullMoonRules.PriestBtnBloodyArrow,
            FullMoonRules.PriestBtnAnkleFreeze,
            FullMoonRules.PriestBtnSummonGuard,
            FullMoonRules.PriestBtnTearOfGoddess,
            FullMoonRules.PriestBtnGodsShield
        };

        for (int i = 0; i < buttons.Length; i++)
            session.Quest.SelectMessageEvents[i] = buttons[i];

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            FullMoonRules.PriestTalkId,
            buttons,
            UserSession.SelectMessageEventCount,
            FullMoonPriestScript);

        await session.Client.SendPacket(dialogPacket);
    }

    private async Task HandlePriestActionAsync(UserSession session, int eventId)
    {
        var (cost, itemId) = eventId switch
        {
            FullMoonRules.PriestBtnDrainRestore => (FullMoonRules.DrainRestoreCost, FullMoonRules.DrainRestoreItem),
            FullMoonRules.PriestBtnFireStrike => (FullMoonRules.FireStrikeCost, FullMoonRules.FireStrikeItem),
            FullMoonRules.PriestBtnBloodyArrow => (FullMoonRules.BloodyArrowCost, FullMoonRules.BloodyArrowItem),
            FullMoonRules.PriestBtnAnkleFreeze => (FullMoonRules.AnkleFreezeCost, FullMoonRules.AnkleFreezeItem),
            FullMoonRules.PriestBtnSummonGuard => (FullMoonRules.SummonGuardCost, FullMoonRules.SummonGuardItem),
            FullMoonRules.PriestBtnTearOfGoddess => (FullMoonRules.TearOfGoddessCost, FullMoonRules.TearOfGoddessItem),
            FullMoonRules.PriestBtnGodsShield => (FullMoonRules.GodsShieldCost, FullMoonRules.GodsShieldItem),
            _ => (0, 0)
        };

        if (itemId == 0)
            return;

        var coinCount = session.Inventory.Where(s => s.ItemId == FullMoonRules.MonsterCoinItem).Sum(s => (int)s.Count);
        if (coinCount < cost)
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.PriestNotEnoughCoinsTalkId]));
            return;
        }

        var item = gameDataService.GetItem(itemId);
        if (item == null)
            return;

        var targetSlot = session.WithLock(s => s.FindSlotForItem(itemId, gameDataService, 1));
        if (targetSlot < 0)
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.PriestInventoryFullTalkId]));
            return;
        }

        if (!await magicItemUsageService.TryConsumeItemAsync(session, FullMoonRules.MonsterCoinItem, cost))
        {
            await session.Client.SendPacket(NpcDialogPacketWriter.NpcSay([FullMoonRules.PriestNotEnoughCoinsTalkId]));
            return;
        }

        await itemGrantService.GrantAsync(session, item, 1);
    }
}


