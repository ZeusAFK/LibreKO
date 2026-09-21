using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using LibreKO.Common.Gameplay;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGameDataAccess(this IServiceCollection services)
    {
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IUserDailyOpRepository, UserDailyOpRepository>();
        services.AddScoped<IKnightsRepository, KnightsRepository>();
        services.AddScoped<IKnightsAllianceRepository, KnightsAllianceRepository>();
        services.AddScoped<IKingElectionRepository, KingElectionRepository>();
        services.AddScoped<ISheriffReportRepository, SheriffReportRepository>();
        services.AddScoped<IDataSeeder, DataSeeder>();
        services.AddScoped<GameDataSeedRunner>();
        services.AddSingleton<IPatchRepository, PatchRepository>();
        services.AddSingleton<IServerRepository, ServerRepository>();
        services.AddSingleton<IGameDataService, GameDataService>();
        return services;
    }

    public static IServiceCollection AddPreGameServices(this IServiceCollection services)
    {
        services.AddScoped<IPreGameService, PreGameService>();
        services.AddScoped<IPreGamePacketCoordinator, PreGamePacketCoordinator>();
        services.AddScoped<IGameSessionInitializer, GameSessionInitializer>();
        return services;
    }

    public static IServiceCollection AddProtocolCoordinators(this IServiceCollection services)
    {
        // Packet coordinators
        services.AddSingleton<IAdminPacketCoordinator, AdminPacketCoordinator>();
        services.AddSingleton<IAdminPanelPacketCoordinator, AdminPanelPacketCoordinator>();
        services.AddSingleton<ICharacterDevelopmentPacketCoordinator, CharacterDevelopmentPacketCoordinator>();
        services.AddSingleton<IJobChangeService, JobChangeService>();
        services.AddSingleton<IClanNtsService, ClanNtsService>();
        services.AddSingleton<IChatPacketCoordinator, ChatPacketCoordinator>();
        services.AddSingleton<ICombatPacketCoordinator, CombatPacketCoordinator>();
        services.AddSingleton<IChallengePacketCoordinator, ChallengePacketCoordinator>();
        services.AddSingleton<IEventSystemsPacketCoordinator, EventSystemsPacketCoordinator>();
        services.AddSingleton<IExchangePacketCoordinator, ExchangePacketCoordinator>();
        services.AddSingleton<IItemPacketCoordinator, ItemPacketCoordinator>();
        services.AddSingleton<IKnightsPacketCoordinator, KnightsPacketCoordinator>();
        services.AddSingleton<IKnightsCapePacketCoordinator, KnightsCapePacketCoordinator>();
        services.AddSingleton<IRebirthPacketCoordinator, RebirthPacketCoordinator>();
        services.AddSingleton<ILootPacketCoordinator, LootPacketCoordinator>();
        services.AddSingleton<IMagicPacketCoordinator, MagicPacketCoordinator>();
        services.AddSingleton<IMerchantPacketCoordinator, MerchantPacketCoordinator>();
        services.AddSingleton<IMiscPacketCoordinator, MiscPacketCoordinator>();
        services.AddSingleton<IAchievementPacketCoordinator, AchievementPacketCoordinator>();
        services.AddSingleton<IAchievementProgressService, AchievementProgressService>();
        services.AddSingleton<ILoyaltyService, LoyaltyService>();
        services.AddSingleton<IMailPacketCoordinator, MailPacketCoordinator>();
        services.AddSingleton<IAuctionPacketCoordinator, AuctionPacketCoordinator>();
        services.AddSingleton<IAttendancePacketCoordinator, AttendancePacketCoordinator>();
        services.AddSingleton<IBountyPacketCoordinator, BountyPacketCoordinator>();
        services.AddSingleton<ITournamentPacketCoordinator, TournamentPacketCoordinator>();
        services.AddSingleton<IDisguisePacketCoordinator, DisguisePacketCoordinator>();
        services.AddSingleton<IMessengerPacketCoordinator, MessengerPacketCoordinator>();
        services.AddSingleton<IForcesPacketCoordinator, ForcesPacketCoordinator>();
        services.AddSingleton<IInstancePacketCoordinator, InstancePacketCoordinator>();
        services.AddSingleton<IChatRoomPacketCoordinator, ChatRoomPacketCoordinator>();
        services.AddSingleton<INationTaxPacketCoordinator, NationTaxPacketCoordinator>();
        services.AddSingleton<IFortunePacketCoordinator, FortunePacketCoordinator>();
        services.AddSingleton<IItemCombinePacketCoordinator, ItemCombinePacketCoordinator>();
        services.AddSingleton<IFishingHallPacketCoordinator, FishingHallPacketCoordinator>();
        services.AddSingleton<IRoulettePacketCoordinator, RoulettePacketCoordinator>();
        services.AddSingleton<IEventBoardPacketCoordinator, EventBoardPacketCoordinator>();
        services.AddSingleton<IDuelPacketCoordinator, DuelPacketCoordinator>();
        services.AddSingleton<IItemExchangePacketCoordinator, ItemExchangePacketCoordinator>();
        services.AddSingleton<IRingUpgradePacketCoordinator, RingUpgradePacketCoordinator>();
        services.AddSingleton<IInnPacketCoordinator, InnPacketCoordinator>();
        services.AddSingleton<IClientSettingsPacketCoordinator, ClientSettingsPacketCoordinator>();
        services.AddSingleton<IGuardPetPacketCoordinator, GuardPetPacketCoordinator>();
        services.AddSingleton<IEventQuestPacketCoordinator, EventQuestPacketCoordinator>();
        services.AddSingleton<IGlobalMapPacketCoordinator, GlobalMapPacketCoordinator>();
        services.AddSingleton<IGeniePacketCoordinator, GeniePacketCoordinator>();
        services.AddSingleton<IGenieSystemPacketCoordinator, GenieSystemPacketCoordinator>();
        services.AddSingleton<IDailyQuestPacketCoordinator, DailyQuestPacketCoordinator>();
        services.AddSingleton<ICollectionRacePacketCoordinator, CollectionRacePacketCoordinator>();
        services.AddSingleton<INationSystemsPacketCoordinator, NationSystemsPacketCoordinator>();
        services.AddSingleton<IQuestPacketCoordinator, QuestPacketCoordinator>();
        services.AddSingleton<IWarehousePacketCoordinator, WarehousePacketCoordinator>();
        services.AddSingleton<IClanWarehousePacketCoordinator, ClanWarehousePacketCoordinator>();
        services.AddSingleton<IVipWarehousePacketCoordinator, VipWarehousePacketCoordinator>();
        services.AddSingleton<IWorldPacketCoordinator, WorldPacketCoordinator>();
        services.AddSingleton<IPartyPacketCoordinator, PartyPacketCoordinator>();
        services.AddSingleton<IPartyBbsPacketCoordinator, PartyBbsPacketCoordinator>();
        services.AddSingleton<IMiningPacketCoordinator, MiningPacketCoordinator>();
        services.AddSingleton<IPetPacketCoordinator, PetPacketCoordinator>();
        services.AddSingleton<ISocialPacketCoordinator, SocialPacketCoordinator>();
        services.AddSingleton<IShoppingMallPacketCoordinator, ShoppingMallPacketCoordinator>();
        services.AddSingleton<IInGameOpcodeRouter, InGameOpcodeRouter>();
        services.AddSingleton<IPacketHandler, GamePacketHandler>();
        return services;
    }

    public static IServiceCollection AddGameDomainServices(this IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IKingEventState, KingEventState>();
        services.AddSingleton<IZoneTransitionService, ZoneTransitionService>();
        services.AddSingleton<ISessionTerminationService, SessionTerminationService>();
        services.AddSingleton<IAccountLockService, AccountLockService>();
        services.AddSingleton<IUserNotificationService, UserNotificationService>();

        // Combat
        services.AddSingleton<ICombatNotificationService, CombatNotificationService>();
        services.AddSingleton<ICombatRewardService, CombatRewardService>();
        services.AddSingleton<ICombatLifecycleService, CombatLifecycleService>();

        // Exchange
        services.AddSingleton<IExchangeLifecycleService, ExchangeLifecycleService>();
        services.AddSingleton<IExchangeTransferService, ExchangeTransferService>();

        // Items
        services.AddSingleton<IItemEquipmentEffectService, ItemEquipmentEffectService>();
        services.AddSingleton<IItemInventoryService, ItemInventoryService>();
        services.AddSingleton<IItemInventoryRuleService, ItemInventoryRuleService>();
        services.AddSingleton<IItemMoveService, ItemMoveService>();
        services.AddSingleton<IItemRemoveService, ItemRemoveService>();
        services.AddSingleton<IItemTradeService, ItemTradeService>();
        services.AddSingleton<IGlobalAnvilRateService, GlobalAnvilRateService>();
        services.AddSingleton<IChaoticGeneratorService, ChaoticGeneratorService>();
        services.AddSingleton<IItemUpgradeService, ItemUpgradeService>();

        // Knights
        services.AddSingleton<IKingElectionPacketService, KingElectionPacketService>();
        services.AddSingleton<IKingGovernancePacketService, KingGovernancePacketService>();
        services.AddSingleton<IKingSystemRuntimeService, KingSystemRuntimeService>();
        services.AddSingleton<IKnightsManagementPacketService, KnightsManagementPacketService>();
        services.AddSingleton<IKnightsMembershipPacketService, KnightsMembershipPacketService>();
        services.AddSingleton<IKnightsRuntimeService, KnightsRuntimeService>();

        // Magic
        services.AddSingleton<MagicMeleeService>();
        services.AddSingleton<MagicRangedService>();
        services.AddSingleton<MagicOverTimeService>();
        services.AddSingleton<MagicAreaService>();
        services.AddSingleton<IMagicCombatEffectService, MagicCombatEffectService>();
        services.AddSingleton<IMagicExecutionService, MagicExecutionService>();
        services.AddSingleton<IMagicItemUsageService, MagicItemUsageService>();
        services.AddSingleton<IMagicMovementEffectService, MagicMovementEffectService>();
        services.AddSingleton<IMagicStatusEffectService, MagicStatusEffectService>();
        services.AddSingleton<IMagicTimingService, MagicTimingService>();
        services.AddSingleton<ISavedMagicService, SavedMagicService>();
        services.AddSingleton<IStealthService, StealthService>();

        // Merchant
        services.AddSingleton<IMerchantInventoryRestoreService, MerchantInventoryRestoreService>();
        services.AddSingleton<IMerchantLifecycleService, MerchantLifecycleService>();
        services.AddSingleton<IMerchantListingService, MerchantListingService>();
        services.AddSingleton<IMerchantBuyingService, MerchantBuyingService>();

        // Shopping mall
        services.AddSingleton<IShoppingMallLetterService, ShoppingMallLetterService>();
        services.AddSingleton<IShoppingMallLetterMutationService, ShoppingMallLetterMutationService>();
        services.AddSingleton<IShoppingMallLetterQueryService, ShoppingMallLetterQueryService>();
        services.AddSingleton<IShoppingMallStoreService, ShoppingMallStoreService>();

        // Quests
        services.AddSingleton<IQuestNpcInteractionService, QuestNpcInteractionService>();
        services.AddSingleton<IQuestProgressionService, QuestProgressionService>();

        // World
        services.AddSingleton<IWorldMovementService, WorldMovementService>();
        services.AddSingleton<IWorldObjectEventService, WorldObjectEventService>();
        services.AddSingleton<IWorldVisibilityService, WorldVisibilityService>();
        services.AddSingleton<IPlayerInspectService, PlayerInspectService>();
        services.AddSingleton<IPlayerProgressionService, PlayerProgressionService>();

        // Character
        services.AddSingleton<CharacterPacketMapper>();
        services.AddSingleton<ICharacterStatePersister, CharacterStatePersister>();
        services.AddSingleton<IUserSessionCharacterMapper, UserSessionCharacterMapper>();

        return services;
    }

    public static IServiceCollection AddWorldServices(this IServiceCollection services)
    {
        services.AddSingleton<IClientFactory>(sp =>
            new ClientFactory(ServerType.Game, sp.GetRequiredService<ILogger<Client>>()));
        services.AddSingleton<MapManager>();
        services.AddSingleton<SessionManager>();

        // NPC AI
        services.AddSingleton<INpcAiBehaviorService, NpcAiBehaviorService>();
        services.AddSingleton<INpcAiCombatService, NpcAiCombatService>();
        services.AddSingleton<INpcAiDeathService, NpcAiDeathService>();
        services.AddSingleton<INpcAiMagicService, NpcAiMagicService>();
        services.AddSingleton<INpcAiMovementService, NpcAiMovementService>();
        services.AddSingleton<IMonsterAggressionPolicy, MonsterAggressionPolicy>();
        services.AddSingleton<INpcAiTargetingService, NpcAiTargetingService>();

        services.AddSingleton<EventSchedulerService>();
        services.AddSingleton<ICollectionRaceService, CollectionRaceService>();
        services.AddSingleton<IScriptEffectApplier, ScriptEffectApplier>();
        services.AddSingleton<LibreKO.Quests.Localization.IQuestTranslations>(provider =>
            QuestTranslationLoader.Load(provider));
        services.AddSingleton<INpcLifecycleService, NpcLifecycleService>();
        services.AddSingleton<QuestScriptEngine>();
        services.AddSingleton<IQuestDefinitionSource>(p => p.GetRequiredService<QuestScriptEngine>());
        services.AddSingleton<IQuestDialogRunner, QuestDialogRunner>();
        services.AddSingleton<IGameServerBootstrapper, GameServerBootstrapper>();

        return services;
    }

    public static IServiceCollection AddGameHostedServices(this IServiceCollection services)
    {
        services.AddSingleton<TimeWeatherBroadcastService>();
        services.AddSingleton<BifrostEventService>();
        services.AddSingleton<IBifrostEventService>(sp => sp.GetRequiredService<BifrostEventService>());
        services.AddHostedService<NpcRespawnService>();
        services.AddHostedService<NpcAiService>();
        services.AddHostedService<MovementBroadcastService>();
        services.AddHostedService<BuffExpiryService>();
        services.AddHostedService<ItemExpiryService>();
        services.AddHostedService<QuestAvailabilityService>();
        services.AddHostedService<HpMpRegenService>();
        services.AddHostedService<AutoSaveService>();
        services.AddHostedService(sp => sp.GetRequiredService<EventSchedulerService>());
        services.AddHostedService<KingElectionTimerService>();
        services.AddHostedService<MonthlyLoyaltyResetService>();
        services.AddHostedService(sp => sp.GetRequiredService<TimeWeatherBroadcastService>());
        services.AddHostedService<HeartbeatProbeService>();
        services.AddHostedService<DailyLoyaltyResetService>();
        services.AddHostedService<ClanGradeRecalcService>();
        services.AddHostedService<ConcurrentPopulationUpdateService>();
        services.AddHostedService<PetSatisfactionTickService>();
        services.AddHostedService<GenieTickService>();
        services.AddHostedService(sp => sp.GetRequiredService<BifrostEventService>());
        services.AddHostedService(sp => sp.GetRequiredService<SocketServer>());
        services.AddHostedService<GracefulShutdownService>();
        return services;
    }

    public static IServiceCollection AddGameSocketServer(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<GameServerSettings>>().Value;
            var clientFactory = sp.GetRequiredService<IClientFactory>();
            var handler = sp.GetRequiredService<IPacketHandler>();
            var logger = sp.GetRequiredService<ILogger<SocketServer>>();

            logger.LogInformation("Starting Game Server on {Host}:{Port}", settings.BindHost, settings.BindPort);

            return new SocketServer(settings.BindHost, settings.BindPort, extraPorts: 0, clientFactory, handler, logger,
                maxConnectionsPerIp: settings.Connections.MaxConnectionsPerIp,
                connectionRateWindowSeconds: settings.Connections.ConnectionRateWindowSeconds,
                maxConnectionAttemptsPerWindow: settings.Connections.MaxConnectionAttemptsPerWindow);
        });
        return services;
    }
}
