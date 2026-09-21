using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Mutable game state
    public DbSet<Account> Accounts { get; set; }
    public DbSet<Server> Servers { get; set; }
    public DbSet<ServerGroup> ServerGroups { get; set; }
    public DbSet<Patch> Patches { get; set; }
    public DbSet<SeedState> SeedStates { get; set; }
    public DbSet<Character> Characters { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<KnightsEntity> Knights { get; set; }
    public DbSet<KnightsAllianceEntity> KnightsAlliances { get; set; }
    public DbSet<Friendship> Friendships { get; set; }
    public DbSet<UserDailyOp> UserDailyOps { get; set; }
    public DbSet<MailBox> MailBoxes { get; set; }
    public DbSet<KingElectionList> KingElectionList { get; set; }
    public DbSet<KingCandidacyNoticeBoard> KingCandidacyNoticeBoard { get; set; }
    public DbSet<KingBallotBox> KingBallotBox { get; set; }
    public DbSet<SheriffReportEntity> SheriffReports { get; set; }
    public DbSet<SheriffVoteEntity> SheriffVotes { get; set; }

    // Static game data
    public DbSet<LevelUpData> LevelUp { get; set; }
    public DbSet<CoefficientData> Coefficients { get; set; }
    public DbSet<StartPositionData> StartPositions { get; set; }
    public DbSet<HomeData> Homes { get; set; }
    public DbSet<ItemData> Items { get; set; }
    public DbSet<WarpData> Warps { get; set; }
    public DbSet<NpcData> Npcs { get; set; }
    public DbSet<NpcPosData> NpcPositions { get; set; }
    public DbSet<MakeItemGroupData> MakeItemGroups { get; set; }
    public DbSet<AttendanceRewardData> AttendanceRewards { get; set; }
    public DbSet<AchievementData> Achievements { get; set; }
    public DbSet<AchievementTitleData> AchievementTitles { get; set; }
    public DbSet<ItemUpgradeData> ItemUpgrades { get; set; }
    public DbSet<ItemUpgradeRecipeData> ItemUpgradeRecipes { get; set; }
    public DbSet<ItemUpgradeSettingsData> ItemUpgradeSettings { get; set; }
    public DbSet<MagicData> Magic { get; set; }
    public DbSet<MagicType1Data> MagicType1 { get; set; }
    public DbSet<MagicType2Data> MagicType2 { get; set; }
    public DbSet<MagicType3Data> MagicType3 { get; set; }
    public DbSet<MagicType4Data> MagicType4 { get; set; }
    public DbSet<MagicType5Data> MagicType5 { get; set; }
    public DbSet<MagicType6Data> MagicType6 { get; set; }
    public DbSet<MagicType7Data> MagicType7 { get; set; }
    public DbSet<MagicType8Data> MagicType8 { get; set; }
    public DbSet<MagicType9Data> MagicType9 { get; set; }
    public DbSet<SetItemData> SetItems { get; set; }
    public DbSet<ItemExchangeData> ItemExchanges { get; set; }
    public DbSet<EventTriggerData> EventTriggers { get; set; }
    public DbSet<NpcItemData> NpcItems { get; set; }
    public DbSet<ItemOpData> ItemOps { get; set; }
    public DbSet<ServerResourceData> ServerResources { get; set; }
    public DbSet<PremiumItemData> PremiumItems { get; set; }
    public DbSet<PremiumItemExpData> PremiumItemExps { get; set; }
    public DbSet<KnightsCapeData> KnightsCapes { get; set; }
    public DbSet<KingSystemData> KingSystem { get; set; }
    public DbSet<MonsterSummonData> MonsterSummons { get; set; }
    public DbSet<ZoneInfoData> ZoneInfos { get; set; }
    public DbSet<GameEventData> GameEvents { get; set; }
    public DbSet<SiegeWarfareData> SiegeWarfare { get; set; }
    public DbSet<CollectionRaceSettingsData> CollectionRaceSettings { get; set; }
    public DbSet<CollectionRaceRewardData> CollectionRaceRewards { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        modelBuilder.ApplyFeatureSchemas();
        base.OnModelCreating(modelBuilder);
    }
}
