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
    IFullMoonRiftService? fullMoonRiftService = null,
    IItemGrantService? itemGrantService = null,
    IMagicItemUsageService? magicItemUsageService = null) : IQuestNpcInteractionService
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

            await HandleBlancActionAsync(session, eventId);
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

        var npcData = gameDataService.GetNpc(npc.NpcId, npc.IsMonster)
            ?? gameDataService.GetNpc(npc.NpcId, !npc.UsesNpcSpawnStyle)
            ?? gameDataService.GetNpc(npc.NpcId, false)
            ?? gameDataService.GetNpc(npc.NpcId, true);
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

        var npcData = gameDataService.GetNpc(npc.NpcId, npc.IsMonster)
            ?? gameDataService.GetNpc(npc.NpcId, !npc.UsesNpcSpawnStyle)
            ?? gameDataService.GetNpc(npc.NpcId, false)
            ?? gameDataService.GetNpc(npc.NpcId, true);
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
        if (npc.NpcId == FullMoonBlancNpcId)
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
    public const int FullMoonActionEnter = 1;
    public const int FullMoonActionClaimVoucher = 2;
    public const int FullMoonActionExchangeMonsterStone = 3;
    public const int FullMoonActionExchangeRedChest = 4;
    public const int FullMoonActionExchangeGreenChest = 5;
    public const int FullMoonActionExchangeBlueChest = 6;
    public const int FullMoonBlancNpcId = 31718;

    private async Task ShowBlancDialogAsync(UserSession session, NpcInstance npc)
    {
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = FullMoonBlancScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        session.Quest.SelectMessageEvents[0] = FullMoonActionEnter;
        session.Quest.SelectMessageEvents[1] = FullMoonActionClaimVoucher;
        session.Quest.SelectMessageEvents[2] = FullMoonActionExchangeMonsterStone;
        session.Quest.SelectMessageEvents[3] = FullMoonActionExchangeRedChest;
        session.Quest.SelectMessageEvents[4] = FullMoonActionExchangeGreenChest;
        session.Quest.SelectMessageEvents[5] = FullMoonActionExchangeBlueChest;

        var jewelCount = session.Inventory.Where(s => s.ItemId == FullMoonRules.RiftJewelItem).Sum(s => s.Count);
        var header = $"[Lunar Order Priest Blanc]\n\nWelcome to the Full Moon Rift!\nGather your party and bring your Full Moon Rift Vouchers to defend the holy nexus against hordes of vicious monsters.\n\nYour Full Moon Rift Jewels: {jewelCount}\n\nDifficulty:\n- 1 to 3 players: Easy (Stage 1-5)\n- 4 to 7 players: Normal (Stage 6-17)\n- 8 players: Hard (Stage 18-35)";

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            -1,
            [-1, -1, -1, -1, -1, -1],
            UserSession.SelectMessageEventCount,
            FullMoonBlancScript,
            header,
            [
                "Enter Full Moon Rift",
                "Claim Full Moon Rift Voucher",
                "Exchange 5 Jewels -> 1x Monster Stone",
                "Exchange 10 Jewels -> 1x Red Treasure Chest",
                "Exchange 20 Jewels -> 1x Green Treasure Chest",
                "Exchange 30 Jewels -> 1x Blue Treasure Chest"
            ]);

        await session.Client.SendPacket(dialogPacket);
    }

    private async Task HandleBlancActionAsync(UserSession session, int eventId)
    {
        if (eventId == FullMoonActionEnter && fullMoonRiftService != null)
        {
            await fullMoonRiftService.HandlePartyEntryAsync(session);
        }
        else if (eventId == FullMoonActionClaimVoucher)
        {
            await ClaimVoucherAsync(session);
        }
        else if (eventId is >= FullMoonActionExchangeMonsterStone and <= FullMoonActionExchangeBlueChest)
        {
            var (cost, rewardItemId, rewardName) = eventId switch
            {
                FullMoonActionExchangeMonsterStone => (5, 900144000, "Monster Stone"),
                FullMoonActionExchangeRedChest => (10, ChaosRules.RedTreasureChest, "Red Treasure Chest"),
                FullMoonActionExchangeGreenChest => (20, ChaosRules.GreenTreasureChest, "Green Treasure Chest"),
                _ => (30, ChaosRules.BlueTreasureChest, "Blue Treasure Chest")
            };

            if (magicItemUsageService != null && await magicItemUsageService.TryConsumeItemAsync(session, FullMoonRules.RiftJewelItem, cost))
            {
                if (gameDataService.GetItem(rewardItemId) is { } rewardItem && itemGrantService != null)
                    await itemGrantService.GrantAsync(session, rewardItem, 1);
                await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                    (byte)session.Nation, $"[Lunar Order Priest Blanc] Exchanged {cost}x Full Moon Rift Jewels for 1x {rewardName}!"));
            }
            else
            {
                await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                    (byte)session.Nation, $"[Lunar Order Priest Blanc] You need at least {cost} Full Moon Rift Jewels to exchange for a {rewardName}."));
            }
        }
    }

    private async Task ClaimVoucherAsync(UserSession session)
    {
        if (session.Inventory.Any(slot => slot.ItemId == FullMoonRules.VoucherItem && slot.Count > 0))
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, "You already have a Full Moon Rift Voucher in your inventory."));
            return;
        }

        if (gameDataService.GetItem(FullMoonRules.VoucherItem) is { } item && itemGrantService != null)
        {
            await itemGrantService.GrantAsync(session, item, 1);
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, "You received a Full Moon Rift Voucher!"));
        }
    }

    private const string FullMoonPriestScript = "fullmoon_priest";
    public const int FullMoonPriestStarter = 1;
    public const int FullMoonPriestBuyBlade = 2;
    public const int FullMoonPriestBuyPit = 3;
    public const int FullMoonPriestBuyHeal = 4;
    public const int FullMoonPriestBlessTower = 5;

    private async Task ShowPriestDialogAsync(UserSession session, NpcInstance npc)
    {
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        session.Quest.ActiveQuestScript = FullMoonPriestScript;
        session.Quest.IsScriptDialog = true;
        Array.Fill(session.Quest.SelectMessageEvents, -1);
        Array.Fill(session.Quest.SelectMessageRewards, -1);

        session.Quest.SelectMessageEvents[0] = FullMoonPriestStarter;
        session.Quest.SelectMessageEvents[1] = FullMoonPriestBuyBlade;
        session.Quest.SelectMessageEvents[2] = FullMoonPriestBuyPit;
        session.Quest.SelectMessageEvents[3] = FullMoonPriestBuyHeal;
        session.Quest.SelectMessageEvents[4] = FullMoonPriestBlessTower;

        var coinCount = session.Inventory.Where(s => s.ItemId == FullMoonRules.MonsterCoinItem).Sum(s => s.Count);
        var header = $"[Priest of Full Moon]\n\nMay the blessing of the Moon strengthen your blade!\nDefeat the invaders to earn Monster Coins. Trade them here for battle skills and tower blessings.\n\nYour Monster Coins: {coinCount}";

        var dialogPacket = NpcDialogPacketWriter.SelectMessage(
            npc.NpcId,
            0,
            -1,
            -1,
            [-1, -1, -1, -1, -1],
            UserSession.SelectMessageEventCount,
            FullMoonPriestScript,
            header,
            [
                "Claim Starter Battle Package (Free)",
                "Buy 5x Killing Blade (2 Coins)",
                "Buy 3x Light Pit [AoE] (3 Coins)",
                "Buy 2x Drain Restore [Heal] (3 Coins)",
                "Bless Nexus Tower [+10,000 HP] (5 Coins)"
            ]);

        await session.Client.SendPacket(dialogPacket);
    }

    private async Task HandlePriestActionAsync(UserSession session, int eventId)
    {
        if (fullMoonRiftService == null)
            return;

        switch (eventId)
        {
            case FullMoonPriestStarter:
                await fullMoonRiftService.GrantStarterPackageAsync(session);
                break;

            case FullMoonPriestBuyBlade:
                if (magicItemUsageService != null && await magicItemUsageService.TryConsumeItemAsync(session, FullMoonRules.MonsterCoinItem, 2))
                {
                    if (gameDataService.GetItem(FullMoonRules.KillingBladeItem) is { } item && itemGrantService != null)
                        await itemGrantService.GrantAsync(session, item, 5);
                    await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)session.Nation, "[Priest of Full Moon] Purchased 5x Killing Blade!"));
                }
                else
                {
                    await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)session.Nation, "[Priest of Full Moon] You need at least 2 Monster Coins to purchase Killing Blades."));
                }
                break;

            case FullMoonPriestBuyPit:
                if (magicItemUsageService != null && await magicItemUsageService.TryConsumeItemAsync(session, FullMoonRules.MonsterCoinItem, 3))
                {
                    if (gameDataService.GetItem(FullMoonRules.LightPitItem) is { } item && itemGrantService != null)
                        await itemGrantService.GrantAsync(session, item, 3);
                    await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)session.Nation, "[Priest of Full Moon] Purchased 3x Light Pit!"));
                }
                else
                {
                    await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)session.Nation, "[Priest of Full Moon] You need at least 3 Monster Coins to purchase Light Pits."));
                }
                break;

            case FullMoonPriestBuyHeal:
                if (magicItemUsageService != null && await magicItemUsageService.TryConsumeItemAsync(session, FullMoonRules.MonsterCoinItem, 3))
                {
                    if (gameDataService.GetItem(FullMoonRules.DrainRestoreItem) is { } item && itemGrantService != null)
                        await itemGrantService.GrantAsync(session, item, 2);
                    await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)session.Nation, "[Priest of Full Moon] Purchased 2x Drain Restore!"));
                }
                else
                {
                    await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)session.Nation, "[Priest of Full Moon] You need at least 3 Monster Coins to purchase Drain Restores."));
                }
                break;

            case FullMoonPriestBlessTower:
                if (magicItemUsageService != null && await magicItemUsageService.TryConsumeItemAsync(session, FullMoonRules.MonsterCoinItem, 5))
                {
                    await fullMoonRiftService.BlessNexusTowerAsync(session, 10_000);
                }
                else
                {
                    await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)session.Nation, "[Priest of Full Moon] You need at least 5 Monster Coins to bless the Nexus Tower."));
                }
                break;
        }
    }
}


