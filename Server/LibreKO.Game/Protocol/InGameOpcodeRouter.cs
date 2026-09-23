using System.Collections.Frozen;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IInGameOpcodeRouter
{
    Func<IClient, Packet, Task>? Resolve(GameOpcodes opcode);
}

public class InGameOpcodeRouter : IInGameOpcodeRouter
{
    private readonly FrozenDictionary<GameOpcodes, Func<IClient, Packet, Task>> _handlers;

    public InGameOpcodeRouter(
        IAdminPacketCoordinator admin,
        IAdminPanelPacketCoordinator adminPanel,
        IClientSettingsPacketCoordinator clientSettings,
        ICombatPacketCoordinator combat,
        IMagicPacketCoordinator magic,
        IChatPacketCoordinator chat,
        ICharacterDevelopmentPacketCoordinator characterDev,
        IChallengePacketCoordinator challenge,
        IEventSystemsPacketCoordinator events,
        IExchangePacketCoordinator exchange,
        IItemPacketCoordinator item,
        IKnightsPacketCoordinator knights,
        IKnightsCapePacketCoordinator knightsCape,
        IRebirthPacketCoordinator rebirth,
        ILootPacketCoordinator loot,
        IMerchantPacketCoordinator merchant,
        IMiscPacketCoordinator misc,
        INationSystemsPacketCoordinator nation,
        IQuestPacketCoordinator quest,
        IWarehousePacketCoordinator warehouse,
        IClanWarehousePacketCoordinator clanWarehouse,
        IVipWarehousePacketCoordinator vipWarehouse,
        IWorldPacketCoordinator world,
        IPartyPacketCoordinator party,
        IPartyBbsPacketCoordinator partyBbs,
        IMiningPacketCoordinator mining,
        IPetPacketCoordinator pet,
        ISocialPacketCoordinator social,
        IShoppingMallPacketCoordinator mall,
        IAchievementPacketCoordinator achievement,
        IMailPacketCoordinator mail,
        IAuctionPacketCoordinator auction,
        IEventBoardPacketCoordinator eventBoard,
        IBountyPacketCoordinator bounty,
        ITournamentPacketCoordinator tournament,
        IDisguisePacketCoordinator disguise,
        IMessengerPacketCoordinator messenger,
        IForcesPacketCoordinator forces,
        IInstancePacketCoordinator instance,
        IChatRoomPacketCoordinator chatRoom,
        INationTaxPacketCoordinator nationTax,
        IFortunePacketCoordinator fortune,
        IItemCombinePacketCoordinator itemCombine,
        IFishingHallPacketCoordinator fishingHall,
        IDuelPacketCoordinator duel,
        IItemExchangePacketCoordinator itemExchange,
        IRingUpgradePacketCoordinator ringUpgrade,
        IInnPacketCoordinator inn,
        IGuardPetPacketCoordinator guardPet,
        IEventQuestPacketCoordinator eventQuest,
        IGlobalMapPacketCoordinator globalMap,
        IGeniePacketCoordinator genie,
        IGenieSystemPacketCoordinator genieSystem,
        IDailyQuestPacketCoordinator dailyQuest,
        ICollectionRacePacketCoordinator collectionRace,
        ILotteryPacketCoordinator lottery,
        SessionManager sessionManager,
        ISessionTerminationService sessionTermination,
        ILogger<InGameOpcodeRouter> logger)
    {
        var map = new Dictionary<GameOpcodes, Func<IClient, Packet, Task>>
        {
            // Combat & stats
            [GameOpcodes.GS_ATTACK] = combat.HandleAttackAsync,
            [GameOpcodes.GS_REGENE] = combat.HandleRegeneAsync,
            [GameOpcodes.GS_SKILLDATA] = combat.HandleSkillDataAsync,
            [GameOpcodes.GS_TARGET_HP] = combat.HandleTargetHpAsync,
            [GameOpcodes.GS_MAGIC_PROCESS] = magic.HandleAsync,
            [GameOpcodes.GS_POINT_CHANGE] = characterDev.HandlePointChangeAsync,
            [GameOpcodes.GS_CLASS_CHANGE] = characterDev.HandleClassChangeAsync,
            [GameOpcodes.GS_HELMET] = characterDev.HandleHelmetAsync,
            [GameOpcodes.GS_SKILLPT_CHANGE] = characterDev.HandleSkillPointChangeAsync,

            // Movement & world
            [GameOpcodes.GS_MOVE] = world.HandleMoveAsync,
            [GameOpcodes.GS_ROTATE] = world.HandleRotateAsync,
            [GameOpcodes.GS_STATE_CHANGE] = world.HandleStateChangeAsync,
            [GameOpcodes.GS_REQ_USERIN] = world.HandleReqUserInAsync,
            [GameOpcodes.GS_REQ_NPCIN] = world.HandleReqNpcInAsync,
            [GameOpcodes.GS_WARP] = world.HandleRecvWarpAsync,
            [GameOpcodes.GS_ZONE_CHANGE] = world.HandleZoneChangeAsync,
            [GameOpcodes.GS_WARP_LIST] = world.HandleWarpListAsync,
            [GameOpcodes.GS_STEALTH] = world.HandleStealthAsync,
            [GameOpcodes.GS_OBJECT_EVENT] = world.HandleObjectEventAsync,
            [GameOpcodes.GS_ACHIEVEMENT] = achievement.HandleAsync,
            [GameOpcodes.GS_MAIL] = mail.HandleAsync,
            [GameOpcodes.GS_AUCTION] = auction.HandleAsync,
            [GameOpcodes.GS_EVENT_BOARD] = eventBoard.HandleAsync,
            [GameOpcodes.GS_BOUNTY] = bounty.HandleAsync,
            [GameOpcodes.GS_TOURNAMENT] = tournament.HandleAsync,
            [GameOpcodes.GS_DISGUISE] = disguise.HandleAsync,
            [GameOpcodes.GS_PRESET] = characterDev.HandlePresetAsync,
            [GameOpcodes.GS_MESSENGER] = messenger.HandleAsync,
            [GameOpcodes.GS_FORCES] = forces.HandleAsync,
            [GameOpcodes.GS_INSTANCE] = instance.HandleAsync,
            [GameOpcodes.GS_CHATROOM] = chatRoom.HandleAsync,
            [GameOpcodes.GS_NATION_TAX] = nationTax.HandleAsync,
            [GameOpcodes.GS_FORTUNE] = fortune.HandleAsync,
            [GameOpcodes.GS_ITEM_COMBINE] = itemCombine.HandleAsync,
            [GameOpcodes.GS_FISHING_HALL] = fishingHall.HandleAsync,
            [GameOpcodes.GS_DUEL] = duel.HandleAsync,
            [GameOpcodes.GS_ITEM_EXCHANGE] = itemExchange.HandleAsync,
            [GameOpcodes.GS_RING_UPGRADE] = ringUpgrade.HandleAsync,
            [GameOpcodes.GS_INN] = inn.HandleAsync,
            [GameOpcodes.GS_GUARD_PET] = guardPet.HandleAsync,
            [GameOpcodes.GS_EVENT_QUEST] = eventQuest.HandleAsync,
            [GameOpcodes.GS_GLOBAL_MAP] = globalMap.HandleAsync,
            [GameOpcodes.GS_GENIE] = genie.HandleAsync,
            [GameOpcodes.GS_GENIE_SYSTEM] = genieSystem.HandleAsync,
            [GameOpcodes.GS_CLIENT_SETTINGS] = clientSettings.HandleAsync,
            [GameOpcodes.GS_HOME] = (c, _) => world.HandleHomeAsync(c),
            [GameOpcodes.GS_REGIONCHANGE] = (c, _) => world.HandleRegionChangeAsync(c),
            [GameOpcodes.GS_NPC_REGION] = (c, _) => world.HandleNpcRegionAsync(c),

            // Items & trade
            [GameOpcodes.GS_ITEM_MOVE] = item.HandleMoveAsync,
            [GameOpcodes.GS_ITEM_REMOVE] = item.HandleRemoveAsync,
            [GameOpcodes.GS_ITEM_REPAIR] = item.HandleRepairAsync,
            [GameOpcodes.GS_ITEM_TRADE] = item.HandleTradeAsync,
            [GameOpcodes.GS_ITEM_UPGRADE] = item.HandleUpgradeAsync,
            [GameOpcodes.GS_ITEM_DROP] = loot.HandleItemDropAsync,
            [GameOpcodes.GS_BUNDLE_OPEN_REQ] = loot.HandleBundleOpenAsync,
            [GameOpcodes.GS_ITEM_GET] = loot.HandleItemGetAsync,
            [GameOpcodes.GS_EXCHANGE] = exchange.HandleAsync,
            [GameOpcodes.GS_MERCHANT] = merchant.HandleAsync,
            [GameOpcodes.GS_WAREHOUSE] = warehouse.HandleAsync,
            [GameOpcodes.GS_CLAN_WAREHOUSE] = clanWarehouse.HandleAsync,
            [GameOpcodes.GS_VIP_WAREHOUSE] = vipWarehouse.HandleAsync,
            [GameOpcodes.GS_SHOPPING_MALL] = mall.HandleAsync,

            // Social
            [GameOpcodes.GS_CHAT_TARGET] = social.HandleChatTargetAsync,
            [GameOpcodes.GS_FRIEND_PROCESS] = social.HandleFriendProcessAsync,
            [GameOpcodes.GS_PARTY] = party.HandleAsync,
            [GameOpcodes.GS_PARTY_BBS] = partyBbs.HandleAsync,

            // Knights/Clan
            [GameOpcodes.GS_KNIGHTS_PROCESS] = knights.HandleProcessAsync,
            [GameOpcodes.GS_KNIGHTS_LIST] = knights.HandleListAsync,
            // GS_CAPE is routed to knightsCape.HandleAsync below (the real cape-purchase
            // handler with clan validation). The earlier stub `knights.HandleCapeAsync`
            // was a no-op and was being overwritten by this dictionary key.

            // Nation & events
            [GameOpcodes.GS_KING] = nation.HandleKingAsync,
            [GameOpcodes.GS_BIFROST] = nation.HandleBifrostAsync,
            [GameOpcodes.GS_RANK] = nation.HandleRankAsync,
            [GameOpcodes.GS_SIEGE] = nation.HandleSiegeAsync,
            [GameOpcodes.GS_EVENT] = events.HandleEventAsync,
            [GameOpcodes.GS_BATTLE_EVENT] = events.HandleBattleEventAsync,
            [GameOpcodes.GS_MAP_EVENT] = events.HandleMapEventAsync,
            [GameOpcodes.GS_PVP] = events.HandlePvpAsync,
            [GameOpcodes.GS_CHALLENGE] = challenge.HandleAsync,

            // Quests
            [GameOpcodes.GS_NPC_EVENT] = quest.HandleNpcEventAsync,
            [GameOpcodes.GS_QUEST] = quest.HandleQuestAsync,
            [GameOpcodes.GS_CLIENT_EVENT] = quest.HandleClientEventAsync,
            [GameOpcodes.GS_SELECT_MSG] = quest.HandleSelectMsgAsync,

            // User info / nearby player list (WIZ_USER_INFORMATIN / BottomUserList)
            [GameOpcodes.GS_USER_INFO] = world.HandleBottomUserListAsync,

            // Admin & system
            [GameOpcodes.GS_OPERATOR] = admin.HandleOperatorAsync,
            [GameOpcodes.GS_ADMIN_PANEL] = adminPanel.HandleAsync,
            [GameOpcodes.GS_LOGOUT] = (c, _) => sessionTermination.LogoutAsync(c),
            [GameOpcodes.GS_DATASAVE] = async (c, _) =>
            {
                var session = sessionManager.GetByClientId(c.Id);
                if (session != null) await sessionTermination.SaveAsync(session);
            },
            // GS_CONCURRENTUSER is routed to misc.HandleConcurrentUserAsync below
            // (the real GM-gated handler). The inline lambda that used to live here
            // sent the count to every caller — kept the GM-only flow via misc.
            [GameOpcodes.GS_SERVER_INDEX] = async (c, _) =>
            {
                var pkt = ServerIndexPacketWriter.Index(
                    ServerIndexPacketWriter.SingleServerId,
                    ServerIndexPacketWriter.SingleServerGroupId);
                await c.SendPacket(pkt);
            },

            // Misc
            [GameOpcodes.GS_PREMIUM] = (c, _) => misc.HandlePremiumAsync(c),
            [GameOpcodes.GS_AUTHORITY_CHANGE] = misc.HandleAuthorityChangeAsync,
            [GameOpcodes.GS_CORPSE] = misc.HandleCorpseAsync,
            [GameOpcodes.GS_MARKET_BBS] = misc.HandleMarketBbsAsync,
            [GameOpcodes.GS_NAME_CHANGE] = misc.HandleNameChangeAsync,
            [GameOpcodes.GS_SANTA] = (c, _) => misc.HandleSantaAsync(c),
            [GameOpcodes.GS_RENTAL] = misc.HandleRentalAsync,
            [GameOpcodes.GS_MINING] = mining.HandleAsync,

            [GameOpcodes.GS_SPEEDHACK_CHECK] = world.HandleSpeedHackCheckAsync,
            [GameOpcodes.GS_CONCURRENTUSER] = misc.HandleConcurrentUserAsync,
            [GameOpcodes.GS_ZONE_CONCURRENT] = misc.HandleZoneConcurrentAsync,
            [GameOpcodes.GS_LOGOSSHOUT] = misc.HandleLogosShoutAsync,
            [GameOpcodes.GS_REPORT] = misc.HandleReportAsync,
            [GameOpcodes.GS_PET] = pet.HandleAsync,
            [GameOpcodes.GS_CAPE] = knightsCape.HandleAsync,
            [GameOpcodes.GS_REBIRTH] = rebirth.HandleAsync,

            // No-ops (acknowledged but no server action)
            // (title sub-system), NOT GenderChange. Sending a real S2C response causes
            // the client to misinterpret it as title data. Drop silently.
            [GameOpcodes.GS_GENDER_CHANGE] = NoOp,
            // Item-upgrade observation: store the item the client wants to watch.
            // Real upgrade-result notices are pushed via WIZ_LOGOSSHOUT later.
            [GameOpcodes.GS_UPGRADE_NOTICE] = (c, p) => WatchUpgradeAsync(sessionManager, c, p),
            // Awakening 0xCB is purely S2C visual; drop any C2S silently.
            [GameOpcodes.GS_AWAKEN] = NoOp,
            // NOT NationTransfer. Sending real S2C nation-transfer packets makes the
            // client display title strings instead. Drop silently.
            [GameOpcodes.GS_NATION_TRANSFER] = NoOp,
            // because there's no daily-quest DB table yet. Same posture here — accept
            // the C2S to suppress unhandled-opcode warnings; S2C builders land when the
            // table does.
            [GameOpcodes.GS_DAILY_QUEST] = dailyQuest.HandleAsync,
            [GameOpcodes.GS_COLLECTION_RACE] = collectionRace.HandleAsync,
            [GameOpcodes.GS_LOTTERY] = lottery.HandleAsync,
            [GameOpcodes.GS_HACKTOOL] = NoOp,
            [GameOpcodes.GS_PROGRAMCHECK] = NoOp,
            [GameOpcodes.GS_REPORT_BUG] = NoOp,
            [GameOpcodes.GS_VIRTUAL_SERVER] = NoOp,
            [GameOpcodes.GS_RECOMMEND_USER] = NoOp,
        };

        _handlers = map.ToFrozenDictionary();
        logger.LogInformation("In-game opcode router initialized with {Count} handlers", _handlers.Count);
    }

    public Func<IClient, Packet, Task>? Resolve(GameOpcodes opcode)
        => _handlers.GetValueOrDefault(opcode);

    private static Task NoOp(IClient client, Packet packet) => Task.CompletedTask;

    private static Task WatchUpgradeAsync(SessionManager sessionManager, IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null) return Task.CompletedTask;
        if (packet.RemainingBytes >= 4)
            session.WatchedUpgradeItem = packet.ReadInt();
        return Task.CompletedTask;
    }
}
