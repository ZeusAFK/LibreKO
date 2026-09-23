using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public class GameDataSeedRunner(IDataSeeder seeder, ILogger<GameDataSeedRunner> logger)
{
    public async Task SeedAllAsync(bool force = false)
    {
        var stopwatch = Stopwatch.StartNew();
        var applied = 0;
        var total = 0;

        async Task Seed<T>(IEntitySeed<T> entitySeed) where T : class
        {
            total++;
            if (await seeder.SeedEntityAsync(entitySeed, force: force))
                applied++;
        }

        logger.LogInformation("Starting game data seeding{Forced}...", force ? " (forced)" : string.Empty);

        await Seed(new LevelUpSeed());
        await Seed(new CoefficientSeed());
        await Seed(new StartPositionSeed());
        await Seed(new HomeSeed());
        await Seed(new ItemSeed());
        await Seed(new PusItemSeed());
        await Seed(new PusCategorySeed());
        // Warps loaded from SMD map files, not DB. See MapManager.GetWarpList().
        await Seed(new NpcSeed());
        await Seed(new NpcPosSeed());
        await Seed(new NpcItemSeed());
        await Seed(new MakeItemGroupSeed());
        await Seed(new AttendanceRewardSeed());
        await Seed(new AchievementSeed());
        await Seed(new AchievementTitleSeed());
        await Seed(new ItemUpgradeSeed());
        await Seed(new ItemUpgradeRecipeSeed());
        await Seed(new ItemUpgradeSettingsSeed());
        await Seed(new MagicSeed());
        await Seed(new MagicType1Seed());
        await Seed(new MagicType2Seed());
        await Seed(new MagicType3Seed());
        await Seed(new MagicType4Seed());
        await Seed(new MagicType5Seed());
        await Seed(new MagicType6Seed());
        await Seed(new MagicType7Seed());
        await Seed(new MagicType8Seed());
        await Seed(new MagicType9Seed());
        await Seed(new SetItemSeed());
        await Seed(new ItemExchangeSeed());
        await Seed(new EventTriggerSeed());
        await Seed(new ItemOpSeed());
        await Seed(new ServerResourceSeed());
        await Seed(new PremiumItemSeed());
        await Seed(new PremiumItemExpSeed());
        await Seed(new KnightsCapeSeed());
        await Seed(new KingSystemSeed());
        await Seed(new MonsterSummonSeed());
        await Seed(new ZoneInfoSeed());
        await Seed(new GameEventSeed());
        await Seed(new SiegeWarfareSeed());
        await Seed(new CollectionRaceSeed());
        await Seed(new CollectionRaceObjectiveSeed());
        await Seed(new CollectionRaceRewardSeed());
        await Seed(new CollectionRaceScheduleSeed());
        await Seed(new LotteryEventSeed());
        await Seed(new LotteryRewardSeed());
        await Seed(new LotteryScheduleSeed());
        await Seed(new UserBotSeed());

        logger.LogInformation(
            "Game data seeding completed: {Applied} of {Total} seeds applied, {Skipped} unchanged, in {Elapsed}ms.",
            applied, total, total - applied, stopwatch.ElapsedMilliseconds);
    }
}
