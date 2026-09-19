using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Startup;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using System.Reflection;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Tests;

public abstract class GameTestBase
{
    protected const int MyInfoItemEntrySize = 19;
    protected const int MyInfoSkillDataSize = 9;

    protected static MapManager CreateMapManagerWithObjectEvent(short zoneId, ObjectEvent objectEvent, params WarpInfo[] warps)
    {
        var mapManager = new MapManager(Substitute.For<Microsoft.Extensions.Logging.ILogger<MapManager>>());
        var map = new SmdFile();
        map.ObjectEvents[objectEvent.Index] = objectEvent;
        foreach (var warp in warps)
            map.Warps[warp.WarpId] = warp;

        var zoneMapsField = typeof(MapManager).GetField("_zoneMaps", BindingFlags.Instance | BindingFlags.NonPublic);
        zoneMapsField.Should().NotBeNull();

        var zoneMaps = zoneMapsField!.GetValue(mapManager).Should().BeOfType<Dictionary<short, SmdFile>>().Subject;
        zoneMaps[zoneId] = map;
        return mapManager;
    }

    protected static MapManager CreateMapManagerWithTiles(short zoneId, int mapSize, float unitDistance, short defaultEventId = 0)
    {
        var mapManager = new MapManager(Substitute.For<Microsoft.Extensions.Logging.ILogger<MapManager>>());
        var map = new SmdFile();

        typeof(SmdFile).GetProperty(nameof(SmdFile.MapSize), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(map, mapSize);
        typeof(SmdFile).GetProperty(nameof(SmdFile.UnitDistance), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(map, unitDistance);
        typeof(SmdFile).GetProperty(nameof(SmdFile.HeightMap), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(map, new float[mapSize * mapSize]);
        typeof(SmdFile).GetProperty(nameof(SmdFile.EventTiles), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .SetValue(map, Enumerable.Repeat(defaultEventId, mapSize * mapSize).ToArray());

        var zoneMapsField = typeof(MapManager).GetField("_zoneMaps", BindingFlags.Instance | BindingFlags.NonPublic);
        zoneMapsField.Should().NotBeNull();

        var zoneMaps = zoneMapsField!.GetValue(mapManager).Should().BeOfType<Dictionary<short, SmdFile>>().Subject;
        zoneMaps[zoneId] = map;
        return mapManager;
    }

    protected static ServiceProvider CreateProvider(
        Action<AppDbContext> seed,
        Action<IGameDataService>? configureGameData = null,
        Action<GameServerSettings>? configureSettings = null)
    {
        var dbRoot = new InMemoryDatabaseRoot();
        var dbName = $"GameTests_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options
            .UseInMemoryDatabase(dbName, dbRoot)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IUserDailyOpRepository, UserDailyOpRepository>();
        services.AddScoped<IKnightsRepository, KnightsRepository>();
        services.AddScoped<IKnightsAllianceRepository, KnightsAllianceRepository>();
        services.AddScoped<IKingElectionRepository, KingElectionRepository>();
        services.AddScoped<ISheriffReportRepository, SheriffReportRepository>();
        services.AddSingleton<IHostEnvironment>(TestHostEnvironmentFactory.Create());
        services.AddScoped<IPreGameService, PreGameService>();
        services.AddScoped<IGameSessionInitializer, GameSessionInitializer>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<CharacterPacketMapper>();
        services.AddSingleton<ICharacterStatePersister, CharacterStatePersister>();
        services.AddSingleton<IUserSessionCharacterMapper, UserSessionCharacterMapper>();
        services.AddSingleton<IKingEventState, KingEventState>();
        services.AddSingleton<TimeWeatherBroadcastService>();
        services.AddSingleton<BifrostEventService>();
        services.AddSingleton<IBifrostEventService>(sp => sp.GetRequiredService<BifrostEventService>());
        services.AddSingleton<IAchievementProgressService, AchievementProgressService>();
        services.AddSingleton<ILoyaltyService, LoyaltyService>();
        services.AddSingleton<IPlayerProgressionService, PlayerProgressionService>();
        services.AddSingleton<IZoneTransitionService, ZoneTransitionService>();
        services.AddSingleton<ISessionTerminationService, SessionTerminationService>();
        services.AddSingleton(CreateServerRepositoryStub());
        services.AddSingleton<IAccountLockService, AccountLockService>();
        services.AddSingleton<IUserNotificationService, UserNotificationService>();
        services.AddProtocolCoordinators();
        services.AddSingleton<ICombatNotificationService, CombatNotificationService>();
        services.AddSingleton<ICombatRewardService, CombatRewardService>();
        services.AddSingleton<ICombatLifecycleService, CombatLifecycleService>();
        services.AddSingleton<IExchangeLifecycleService, ExchangeLifecycleService>();
        services.AddSingleton<IExchangeTransferService, ExchangeTransferService>();
        services.AddSingleton<IItemEquipmentEffectService, ItemEquipmentEffectService>();
        services.AddSingleton<IItemInventoryService, ItemInventoryService>();
        services.AddSingleton<IItemInventoryRuleService, ItemInventoryRuleService>();
        services.AddSingleton<IItemMoveService, ItemMoveService>();
        services.AddSingleton<IItemRemoveService, ItemRemoveService>();
        services.AddSingleton<IItemTradeService, ItemTradeService>();
        services.AddSingleton<IGlobalAnvilRateService, GlobalAnvilRateService>();
        services.AddSingleton<IChaoticGeneratorService, ChaoticGeneratorService>();
        services.AddSingleton<IItemUpgradeService, ItemUpgradeService>();
        services.AddSingleton<IKingElectionPacketService, KingElectionPacketService>();
        services.AddSingleton<IKingGovernancePacketService, KingGovernancePacketService>();
        services.AddSingleton<IKingSystemRuntimeService, KingSystemRuntimeService>();
        services.AddSingleton<IKnightsManagementPacketService, KnightsManagementPacketService>();
        services.AddSingleton<IKnightsMembershipPacketService, KnightsMembershipPacketService>();
        services.AddSingleton<IKnightsRuntimeService, KnightsRuntimeService>();
        services.AddSingleton<IMerchantInventoryRestoreService, MerchantInventoryRestoreService>();
        services.AddSingleton<IMerchantLifecycleService, MerchantLifecycleService>();
        services.AddSingleton<IMerchantListingService, MerchantListingService>();
        services.AddSingleton<IMerchantBuyingService, MerchantBuyingService>();
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
        services.AddSingleton<IQuestNpcInteractionService, QuestNpcInteractionService>();
        services.AddSingleton<IQuestProgressionService, QuestProgressionService>();
        services.AddSingleton<IWorldMovementService, WorldMovementService>();
        services.AddSingleton<IWorldObjectEventService, WorldObjectEventService>();
        services.AddSingleton<IWorldVisibilityService, WorldVisibilityService>();
        services.AddSingleton<IPlayerInspectService, PlayerInspectService>();
        services.AddSingleton<IShoppingMallLetterService, ShoppingMallLetterService>();
        services.AddSingleton<IShoppingMallLetterMutationService, ShoppingMallLetterMutationService>();
        services.AddSingleton<IShoppingMallLetterQueryService, ShoppingMallLetterQueryService>();
        services.AddSingleton<IShoppingMallStoreService, ShoppingMallStoreService>();
        services.AddScoped<IPreGamePacketCoordinator, PreGamePacketCoordinator>();
        services.AddSingleton<INpcAiBehaviorService, NpcAiBehaviorService>();
        services.AddSingleton<INpcAiCombatService, NpcAiCombatService>();
        services.AddSingleton<INpcAiDeathService, NpcAiDeathService>();
        services.AddSingleton<INpcAiMagicService, NpcAiMagicService>();
        services.AddSingleton<INpcAiMovementService, NpcAiMovementService>();
        services.AddSingleton<IMonsterAggressionPolicy, MonsterAggressionPolicy>();
        services.AddSingleton<INpcAiTargetingService, NpcAiTargetingService>();
        services.AddSingleton<EventSchedulerService>();
        services.AddSingleton<MovementBroadcastService>();
        services.Configure<GameServerSettings>(settings =>
        {
            settings.Version = 2618;
            configureSettings?.Invoke(settings);
        });
        services.AddSingleton<LibreKO.Game.Scripting.IScriptEffectApplier,
            LibreKO.Game.Scripting.ScriptEffectApplier>();
        services.AddSingleton<LibreKO.Quests.Localization.IQuestTranslations>(
            LibreKO.Quests.Localization.QuestTranslations.Empty);
        services.AddSingleton<LibreKO.Game.Scripting.QuestScriptEngine>();
        services.AddSingleton<LibreKO.Game.Scripting.IQuestDefinitionSource>(
            p => p.GetRequiredService<LibreKO.Game.Scripting.QuestScriptEngine>());
        services.AddSingleton<LibreKO.Game.Scripting.IQuestDialogRunner,
            LibreKO.Game.Scripting.QuestDialogRunner>();

        var gameData = Substitute.For<IGameDataService>();
        gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData>());
        configureGameData?.Invoke(gameData);
        services.AddSingleton(gameData);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        seed(db);
        db.SaveChanges();

        return provider;
    }

    protected static IAccountLockService CreateOwningAccountLock()
    {
        var accountLock = Substitute.For<IAccountLockService>();
        accountLock.Owns(Arg.Any<IClient>()).Returns(true);
        accountLock
            .AcquireAsync(Arg.Any<IClient>(), Arg.Any<int>())
            .Returns(Task.FromResult(new AccountLockResult(true, null)));
        return accountLock;
    }

    private static IServerRepository CreateServerRepositoryStub()
    {
        var repository = Substitute.For<IServerRepository>();
        repository.GetServers().Returns(Task.FromResult(new List<LibreKO.Common.Domain.Entities.Server>()));
        return repository;
    }

    protected static CoefficientData CreateBasicCoefficient(short classId)
    {
        return new CoefficientData
        {
            ClassId = classId,
            ShortSword = 1,
            Sword = 1,
            Axe = 1,
            Club = 1,
            Spear = 1,
            Staff = 1,
            Bow = 1,
            Hp = 1,
            Mp = 1,
            Ac = 1,
            Hitrate = 1,
            Evasionrate = 1
        };
    }

    protected static async Task<int> GetAccountIdAsync(IServiceProvider provider, string login)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Accounts.Where(account => account.Login == login).Select(account => account.Id).SingleAsync();
    }

    protected static async Task<int> GetCharacterIdAsync(IServiceProvider provider, string name)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Characters.Where(character => character.Name == name).Select(character => character.Id).SingleAsync();
    }

    protected static Packet ClonePacket(Packet packet)
    {
        var clone = new Packet(packet.GetOpcode());
        clone.WriteBytes(packet.GetData());
        clone.ResetOffset();
        return clone;
    }

    protected sealed class TemporaryQuestScripts : IDisposable
    {
        public const string NestedFolder = "rewards";

        public string Directory { get; }

        public TemporaryQuestScripts(string fileName, string source)
        {
            Directory = Path.Combine(Path.GetTempPath(), $"ko-quests-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, NestedFolder));
            File.WriteAllText(Path.Combine(Directory, NestedFolder, fileName), source);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, recursive: true); }
            catch (IOException) { }
        }
    }

    protected static TemporaryQuestScripts ScriptedKillQuest(int questId, int monsterId, int count) =>
        new($"quest{questId}.quest", $"""
            Bind Npc 9000

            Quest {questId}
                Kill {count} of {monsterId}

            On greeting
                Say "Kill things."
                Topic "Right" goto close
            """);

    protected static byte[] CreateInventory(params (int Slot, int ItemId, short Durability)[] items)
    {
        var data = new byte[InventoryConstants.InventoryTotal * 8];
        foreach (var (Slot, ItemId, Durability) in items)
        {
            var offset = Slot * 8;
            BitConverter.TryWriteBytes(data.AsSpan(offset), ItemId);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 4), Durability);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 6), (ushort)1);
        }

        return data;
    }

    protected static byte[] CreateWarehouse(params (int Slot, int ItemId, short Durability, ushort Count)[] items)
    {
        var data = new byte[UserSession.WarehouseMax * 8];
        foreach (var (Slot, ItemId, Durability, Count) in items)
        {
            var offset = Slot * 8;
            BitConverter.TryWriteBytes(data.AsSpan(offset), ItemId);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 4), Durability);
            BitConverter.TryWriteBytes(data.AsSpan(offset + 6), Count);
        }

        return data;
    }

    protected static CharacterInfoEntry ReadCharacterInfo(Packet packet)
    {
        var name = packet.ReadString();
        _ = packet.ReadByte();   // race
        _ = packet.ReadShort();  // class
        _ = packet.ReadByte();   // level
        _ = packet.ReadByte();   // face
        _ = packet.ReadByte();   // unknown
        _ = packet.ReadInt();    // hair
        _ = packet.ReadShort();  // zone

        var equipment = new (int ItemId, short Durability)[17];
        for (var i = 0; i < equipment.Length; i++)
            equipment[i] = (packet.ReadInt(), packet.ReadShort());

        return new CharacterInfoEntry(name, equipment);
    }

    protected static (byte Authority, byte AccountStatus, short PremiumHours) ReadMyInfoAuthorityAndPremium(Packet packet)
    {
        SkipMyInfoToAuthority(packet);
        var authority = packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadBytes(MyInfoSkillDataSize);
        packet.ReadBytes(InventoryConstants.MyInfoWireTotal * MyInfoItemEntrySize);

        var accountStatus = packet.ReadByte();
        var premiumCount = packet.ReadByte();
        short premiumHours = 0;
        for (var i = 0; i < premiumCount; i++)
        {
            packet.ReadByte();
            if (i == 0)
                premiumHours = packet.ReadShort();
            else
                packet.ReadShort();
        }

        return (authority, accountStatus, premiumHours);
    }

    protected static ushort ReadMyInfoNoClanCape(Packet packet)
    {
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadSByteString();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadLong();
        packet.ReadLong();
        packet.ReadInt();
        packet.ReadInt();
        var knightsId = packet.ReadShort();
        packet.ReadByte();

        if (knightsId != 0)
            throw new InvalidOperationException("MYINFO packet has clan data.");

        packet.ReadLong();
        return packet.ReadUShort();
    }

    protected static (short MaxHp, short Hp, short MaxMp, short Mp) ReadMyInfoVitals(Packet packet)
    {
        SkipMyInfoToVitals(packet);

        return (packet.ReadShort(), packet.ReadShort(), packet.ReadShort(), packet.ReadShort());
    }

    protected static (int ItemId, short Durability, short Count)[] ReadMyInfoItems(Packet packet)
    {
        SkipMyInfoToItems(packet);
        var items = new (int ItemId, short Durability, short Count)[InventoryConstants.InventoryTotal];
        for (var wireIndex = 0; wireIndex < InventoryConstants.MyInfoWireTotal; wireIndex++)
        {
            var itemId = packet.ReadInt();
            var durability = packet.ReadShort();
            var count = packet.ReadShort();
            packet.ReadByte();
            packet.ReadShort();
            packet.ReadInt();
            packet.ReadInt();
            items[InventoryConstants.MyInfoWireSlot(wireIndex)] = (itemId, durability, count);
        }

        return items;
    }

    protected static void SkipMyInfoToVitals(Packet packet)
    {
        SkipMyInfoHeader(packet);
        packet.ReadLong();
    }

    protected static void SkipMyInfoToAuthority(Packet packet)
    {
        SkipMyInfoToVitals(packet);
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadInt();
        packet.ReadInt();
        for (var i = 0; i < 10; i++)
            packet.ReadByte();
        packet.ReadShort();
        packet.ReadShort();
        for (var i = 0; i < 6; i++)
            packet.ReadByte();
        packet.ReadInt();
    }

    protected static void SkipMyInfoToItems(Packet packet)
    {
        SkipMyInfoToAuthority(packet);
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadBytes(MyInfoSkillDataSize);
    }

    protected static void SkipMyInfoHeader(Packet packet)
    {
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadSByteString();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadLong();
        packet.ReadLong();
        packet.ReadInt();
        packet.ReadInt();
        var knightsId = packet.ReadShort();
        packet.ReadByte();

        if (knightsId != 0)
        {
            packet.ReadShort();
            packet.ReadByte();
            packet.ReadSByteString();
            packet.ReadByte();
            packet.ReadByte();
            packet.ReadShort();
            packet.ReadShort();
            packet.ReadByte();
            packet.ReadByte();
            packet.ReadByte();
            packet.ReadByte();
        }
        else
        {
            packet.ReadLong();
            packet.ReadShort();
            packet.ReadInt();
        }
    }

    protected static (
        MagicProcessOpcode ProcessOpcode,
        int SkillId,
        int CasterId,
        int TargetId,
        int[] Data) ReadMagicProcessPacket(Packet packet)
    {
        packet.ResetOffset();

        var data = new int[7];
        var processOpcode = (MagicProcessOpcode)packet.ReadByte();
        var skillId = packet.ReadInt();
        var casterId = packet.ReadInt();
        var targetId = packet.ReadInt();
        for (var i = 0; i < data.Length; i++)
            data[i] = (short)packet.ReadInt();

        packet.RemainingBytes.Should().Be(0);
        return (processOpcode, skillId, casterId, targetId, data);
    }

    protected sealed record CharacterInfoEntry(string Name, (int ItemId, short Durability)[] Equipment);
}

internal static class TestHostEnvironmentFactory
{
    public static IHostEnvironment Create(string? contentRootPath = null)
    {
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.ApplicationName.Returns("LibreKO.Game.Tests");
        hostEnvironment.EnvironmentName.Returns("Test");
        hostEnvironment.ContentRootPath.Returns(contentRootPath ?? Directory.GetCurrentDirectory());
        return hostEnvironment;
    }
}
