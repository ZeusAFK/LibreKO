using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

internal static class ModelBuilderExtensions
{
    public static void ApplyFeatureSchemas(this ModelBuilder modelBuilder)
    {
        Configure<Account>(modelBuilder, "Accounts");
        Configure<Character>(modelBuilder, "Characters");
        Configure<Friendship>(modelBuilder, "Friendships");
        Configure<MailBox>(modelBuilder, "MailBoxes");
        Configure<UserDailyOp>(modelBuilder, "UserDailyOps");
        Configure<Warehouse>(modelBuilder, "Warehouses");

        Configure<KnightsEntity>(modelBuilder, "Knights");
        Configure<KnightsAllianceEntity>(modelBuilder, "KnightsAlliances");
        Configure<KnightsCapeData>(modelBuilder, "KnightsCapes");

        Configure<KingBallotBox>(modelBuilder, "KingBallotBoxes");
        Configure<KingCandidacyNoticeBoard>(modelBuilder, "KingCandidacyNoticeBoards");
        Configure<KingElectionList>(modelBuilder, "KingElectionLists");
        Configure<KingSystemData>(modelBuilder, "KingSystem");
        Configure<SiegeWarfareData>(modelBuilder, "SiegeWarfare");

        Configure<CoefficientData>(modelBuilder, "Coefficients");
        Configure<LevelUpData>(modelBuilder, "LevelUp");
        Configure<HomeData>(modelBuilder, "Homes");

        Configure<ItemData>(modelBuilder, "Items");
        Configure<ItemExchangeData>(modelBuilder, "ItemExchanges");
        Configure<ItemOpData>(modelBuilder, "ItemOps");
        Configure<ItemUpgradeData>(modelBuilder, "ItemUpgrades");
        Configure<ItemUpgradeSettingsData>(modelBuilder, "ItemUpgradeSettings");
        Configure<ItemUpgradeRecipeData>(modelBuilder, "ItemUpgradeRecipes");
        Configure<PremiumItemData>(modelBuilder, "PremiumItems");
        Configure<PremiumItemExpData>(modelBuilder, "PremiumItemExps");
        Configure<PusItemData>(modelBuilder, "PusItems");
        Configure<PusCategoryData>(modelBuilder, "PusCategories");
        Configure<SetItemData>(modelBuilder, "SetItems");

        Configure<MagicData>(modelBuilder, "Magic");
        Configure<MagicType1Data>(modelBuilder, "MagicType1");
        Configure<MagicType2Data>(modelBuilder, "MagicType2");
        Configure<MagicType3Data>(modelBuilder, "MagicType3");
        Configure<MagicType4Data>(modelBuilder, "MagicType4");
        Configure<MagicType5Data>(modelBuilder, "MagicType5");
        Configure<MagicType6Data>(modelBuilder, "MagicType6");
        Configure<MagicType7Data>(modelBuilder, "MagicType7");
        Configure<MagicType8Data>(modelBuilder, "MagicType8");
        Configure<MagicType9Data>(modelBuilder, "MagicType9");


        Configure<EventTriggerData>(modelBuilder, "EventTriggers");
        Configure<GameEventData>(modelBuilder, "GameEvents");
        Configure<MonsterSummonData>(modelBuilder, "MonsterSummons");
        Configure<NpcData>(modelBuilder, "Npcs");
        Configure<NpcItemData>(modelBuilder, "NpcItems");
        Configure<NpcPosData>(modelBuilder, "NpcPositions");
        Configure<MakeItemGroupData>(modelBuilder, "MakeItemGroups");
        Configure<AttendanceRewardData>(modelBuilder, "AttendanceRewards");
        Configure<AchievementData>(modelBuilder, "Achievements");
        Configure<AchievementTitleData>(modelBuilder, "AchievementTitles");
        Configure<StartPositionData>(modelBuilder, "StartPositions");
        Configure<WarpData>(modelBuilder, "Warps");
        Configure<ZoneInfoData>(modelBuilder, "ZoneInfos");

        Configure<Patch>(modelBuilder, "Patches");
        Configure<SeedState>(modelBuilder, "SeedStates");
        Configure<Server>(modelBuilder, "Servers");
        Configure<ServerResourceData>(modelBuilder, "ServerResources");
    }

    private static void Configure<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : class
    {
        modelBuilder.Entity<TEntity>().ToTable(tableName);
    }
}
