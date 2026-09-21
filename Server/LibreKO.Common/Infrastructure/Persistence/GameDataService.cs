using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Persistence.Seed.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Common.Infrastructure.Persistence;

public class GameDataService(IServiceScopeFactory scopeFactory, ILogger<GameDataService> logger) : IGameDataService
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public IReadOnlyDictionary<byte, long> LevelUpTable { get; private set; } = new Dictionary<byte, long>();
    public IReadOnlyDictionary<short, CoefficientData> CoefficientTable { get; private set; } = new Dictionary<short, CoefficientData>();
    public IReadOnlyDictionary<short, StartPositionData> StartPositionTable { get; private set; } = new Dictionary<short, StartPositionData>();
    public IReadOnlyDictionary<int, ItemData> ItemTable { get; private set; } = new Dictionary<int, ItemData>();
    public IReadOnlyDictionary<int, NpcData> NpcTable { get; private set; } = new Dictionary<int, NpcData>();
    public IReadOnlyDictionary<int, NpcData> MonsterTable { get; private set; } = new Dictionary<int, NpcData>();
    public IReadOnlyList<NpcPosData> NpcPositions { get; private set; } = [];
    // Warps are loaded from SMD map files, not the database. See MapManager.GetWarpList().
    public IReadOnlyDictionary<int, ItemUpgradeData> UpgradeTable { get; private set; } = new Dictionary<int, ItemUpgradeData>();
    public ILookup<int, ItemUpgradeRecipeData> UpgradeRecipesByOrigin { get; private set; } = Enumerable.Empty<ItemUpgradeRecipeData>().ToLookup(x => x.OriginNumber);
    public IReadOnlyList<ItemUpgradeSettingsData> UpgradeSettings { get; private set; } = [];
    public IReadOnlyDictionary<int, MagicData> MagicTable { get; private set; } = new Dictionary<int, MagicData>();
    public IReadOnlyDictionary<int, MagicType1Data> MagicType1Table { get; private set; } = new Dictionary<int, MagicType1Data>();
    public IReadOnlyDictionary<int, MagicType2Data> MagicType2Table { get; private set; } = new Dictionary<int, MagicType2Data>();
    public IReadOnlyDictionary<int, MagicType3Data> MagicType3Table { get; private set; } = new Dictionary<int, MagicType3Data>();
    public IReadOnlyDictionary<int, MagicType4Data> MagicType4Table { get; private set; } = new Dictionary<int, MagicType4Data>();
    public IReadOnlyDictionary<int, MagicType5Data> MagicType5Table { get; private set; } = new Dictionary<int, MagicType5Data>();
    public IReadOnlyDictionary<int, MagicType6Data> MagicType6Table { get; private set; } = new Dictionary<int, MagicType6Data>();
    public IReadOnlyDictionary<int, MagicType7Data> MagicType7Table { get; private set; } = new Dictionary<int, MagicType7Data>();
    public IReadOnlyDictionary<int, MagicType8Data> MagicType8Table { get; private set; } = new Dictionary<int, MagicType8Data>();
    public IReadOnlyDictionary<int, MagicType9Data> MagicType9Table { get; private set; } = new Dictionary<int, MagicType9Data>();
    public IReadOnlyDictionary<int, SetItemData> SetItemTable { get; private set; } = new Dictionary<int, SetItemData>();
    public IReadOnlyDictionary<int, ItemExchangeData> ItemExchangeTable { get; private set; } = new Dictionary<int, ItemExchangeData>();
    public IReadOnlyDictionary<int, ItemExchangeExpData> ItemExchangeExpTable { get; private set; } = new Dictionary<int, ItemExchangeExpData>();
    public ILookup<(byte OreType, short NpcId), MiningExchangeData> MiningExchangesByOreNpc { get; private set; }
        = Enumerable.Empty<MiningExchangeData>().ToLookup(x => ((byte)0, (short)0));
    public ILookup<(GatherType Type, GatherTool Tool, GatherWarStatus War), MiningFishingItemData> MiningFishingItemsByPool { get; private set; }
        = Enumerable.Empty<MiningFishingItemData>().ToLookup(x => (GatherType.Mining, GatherTool.Plain, GatherWarStatus.Peace));
    public IReadOnlyDictionary<int, EventTriggerData> EventTriggerTable { get; private set; } = new Dictionary<int, EventTriggerData>();
    private IReadOnlyDictionary<(short NpcType, int TrapNumber), int> _eventTriggersByNpc = new Dictionary<(short, int), int>();
    public IReadOnlyDictionary<(short Index, bool IsMonster), NpcItemData> NpcItemTable { get; private set; } = new Dictionary<(short, bool), NpcItemData>();
    public IReadOnlyDictionary<int, int[]> MakeItemGroupTable { get; private set; } = new Dictionary<int, int[]>();
    public IReadOnlyDictionary<int, AttendanceRewardData> AttendanceRewardTable { get; private set; } = new Dictionary<int, AttendanceRewardData>();
    public IReadOnlyDictionary<int, AchievementData> AchievementTable { get; private set; } = new Dictionary<int, AchievementData>();
    public IReadOnlyDictionary<int, AchievementTitleData> AchievementTitleTable { get; private set; } = new Dictionary<int, AchievementTitleData>();
    public IReadOnlyDictionary<int, CollectionRaceSettingsData> CollectionRaceSettingsTable { get; private set; } = new Dictionary<int, CollectionRaceSettingsData>();
    public ILookup<int, CollectionRaceRewardData> CollectionRaceRewardsByEventIndex { get; private set; } = Enumerable.Empty<CollectionRaceRewardData>().ToLookup(x => x.EventIndex);
    public ILookup<int, ItemOpData> ItemOpsByItemId { get; private set; } = Enumerable.Empty<ItemOpData>().ToLookup(x => x.ItemId);
    public IReadOnlyDictionary<int, string> ServerResourceTable { get; private set; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<byte, PremiumItemData> PremiumItemTable { get; private set; } = new Dictionary<byte, PremiumItemData>();
    public IReadOnlyList<PremiumItemExpData> PremiumItemExpTable { get; private set; } = [];
    public IReadOnlyDictionary<short, KnightsCapeData> KnightsCapeTable { get; private set; } = new Dictionary<short, KnightsCapeData>();
    public IReadOnlyDictionary<byte, KingSystemData> KingSystemTable { get; private set; } = new Dictionary<byte, KingSystemData>();
    public IReadOnlyList<MonsterSummonData> MonsterSummonTable { get; private set; } = [];
    public IReadOnlyDictionary<short, ZoneInfoData> ZoneInfoTable { get; private set; } = new Dictionary<short, ZoneInfoData>();
    public ILookup<byte, GameEventData> GameEventsByZone { get; private set; } = Enumerable.Empty<GameEventData>().ToLookup(x => x.ZoneNum);
    public SiegeWarfareData? SiegeWarfare { get; private set; }
    public bool IsLoaded { get; private set; }

    private void WarnOnIncompleteLevelTable()
    {
        var missing = new List<byte>();
        for (var level = ProgressionTable.MinLevel; level < ProgressionTable.MaxLevel; level++)
        {
            if (!LevelUpTable.ContainsKey(level))
                missing.Add(level);
        }

        if (missing.Count > 0)
        {
            logger.LogWarning(
                "The level table is missing {Count} of {Total} levels, starting at {First}. "
                + "A character reaching one of them cannot level further, which is indistinguishable "
                + "from reaching the cap.",
                missing.Count, ProgressionTable.MaxLevel - ProgressionTable.MinLevel, missing[0]);
        }
    }

    public long GetMaxExpForLevel(byte level)
    {
        return LevelUpTable.TryGetValue(level, out var exp) ? exp : long.MaxValue;
    }

    public CoefficientData? GetCoefficient(short classId)
    {
        return CoefficientTable.TryGetValue(classId, out var data) ? data : null;
    }

    public StartPositionData? GetStartPosition(short zoneId)
    {
        if (StartPositionTable.TryGetValue(zoneId, out var data))
            return data;

        var sourceZone = ResolveSharedMapDataZone(zoneId, StartPositionTable.Keys);
        return sourceZone != zoneId && StartPositionTable.TryGetValue(sourceZone, out data)
            ? data
            : null;
    }

    public ItemData? GetItem(int itemId)
    {
        return ItemTable.TryGetValue(itemId, out var data) ? data : null;
    }

    public NpcData? GetNpc(int npcId)
    {
        if (MonsterTable.TryGetValue(npcId, out var monster))
            return monster;

        return NpcTable.TryGetValue(npcId, out var npc) ? npc : null;
    }

    public NpcData? GetNpc(int npcId, bool isMonster)
    {
        var table = isMonster ? MonsterTable : NpcTable;
        return table.TryGetValue(npcId, out var data) ? data : null;
    }

    public NpcData? GetSpawnProto(NpcPosData pos)
    {
        return GetNpc(pos.NpcId, pos.ActType < NpcPosData.NpcSpawnActTypeBase);
    }


    public ItemUpgradeData? GetUpgrade(int index)
    {
        return UpgradeTable.TryGetValue(index, out var data) ? data : null;
    }

    public ItemUpgradeRecipeData? GetUpgradeRecipe(int originItemId, int requiredItemId)
    {
        foreach (var recipe in UpgradeRecipesByOrigin[originItemId])
            if (recipe.RequiredItem == requiredItemId)
                return recipe;

        return null;
    }

    public ItemUpgradeSettingsData? GetUpgradeSetting(short itemType, short grade, int firstMaterial, int secondMaterial)
    {
        foreach (var setting in UpgradeSettings)
            if (setting.ItemType == itemType && setting.MatchesGrade(grade) && setting.MatchesMaterials(firstMaterial, secondMaterial))
                return setting;

        return null;
    }

    public MagicData? GetMagic(int magicId)
    {
        return MagicTable.TryGetValue(magicId, out var data) ? data : null;
    }

    public SetItemData? GetSetItem(int setIndex)
    {
        return SetItemTable.TryGetValue(setIndex, out var data) ? data : null;
    }

    public ItemExchangeData? GetItemExchange(int index)
    {
        return ItemExchangeTable.TryGetValue(index, out var data) ? data : null;
    }

    public ItemExchangeExpData? GetItemExchangeExp(int index)
    {
        return ItemExchangeExpTable.TryGetValue(index, out var data) ? data : null;
    }

    public int GetEventTrigger(short npcType, short trapNumber)
    {
        return _eventTriggersByNpc.TryGetValue((npcType, trapNumber), out var triggerNum)
            ? triggerNum
            : EventTriggerData.NoTrigger;
    }

    public NpcItemData? GetNpcItem(short index, bool isMonster)
    {
        return NpcItemTable.TryGetValue((index, isMonster), out var data) ? data : null;
    }

    public IEnumerable<ItemOpData> GetItemOps(int itemId)
    {
        return ItemOpsByItemId[itemId];
    }

    public string? GetServerResource(int resourceId)
    {
        return ServerResourceTable.TryGetValue(resourceId, out var res) ? res : null;
    }

    public int GetPremiumProperty(byte premiumType, PremiumPropertyType property)
    {
        if (premiumType == 0)
            return 0;
        if (!PremiumItemTable.TryGetValue(premiumType, out var premium))
            return 0;

        return property switch
        {
            PremiumPropertyType.Noah => premium.NoahPercent,
            PremiumPropertyType.Drop => premium.DropPercent,
            PremiumPropertyType.RepairDiscount => premium.RepairDiscountPercent,
            PremiumPropertyType.ItemSell => premium.ItemSellPercent,
            _ => 0
        };
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        IsLoaded = false;
        await LoadAsync(cancellationToken);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoaded)
            return;

        await _loadLock.WaitAsync(cancellationToken);

        try
        {
            if (IsLoaded)
                return;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            LevelUpTable = await LoadDictionaryAsync(db.LevelUp, x => x.Level, x => x.Exp, "level-up entries", cancellationToken);
            WarnOnIncompleteLevelTable();
            CoefficientTable = await LoadDictionaryAsync(db.Coefficients, x => x.ClassId, "coefficient entries", cancellationToken);
            StartPositionTable = await LoadDictionaryAsync(db.StartPositions, x => x.ZoneId, "start position entries", cancellationToken);
            ItemTable = await LoadDictionaryAsync(db.Items, x => x.Num, "item entries", cancellationToken);
            // Warps loaded from SMD map files, not DB. See MapManager.GetWarpList().
            NpcTable = await LoadDictionaryAsync(
                db.Npcs.Where(x => !x.IsMonster), x => x.Id, "NPC entries", cancellationToken);
            MonsterTable = await LoadDictionaryAsync(
                db.Npcs.Where(x => x.IsMonster), x => x.Id, "monster entries", cancellationToken);
            NpcPositions = await LoadListAsync(
                db.NpcPositions.OrderBy(x => x.Index),
                "NPC position entries",
                cancellationToken);
            UpgradeTable = await LoadDictionaryAsync(db.ItemUpgrades, x => x.Index, "item upgrade entries", cancellationToken);
            var upgradeRecipes = await LoadListAsync(db.ItemUpgradeRecipes, "item upgrade recipe entries", cancellationToken);
            UpgradeRecipesByOrigin = upgradeRecipes.ToLookup(x => x.OriginNumber);
            UpgradeSettings = await LoadListAsync(db.ItemUpgradeSettings, "item upgrade setting entries", cancellationToken);
            MagicTable = await LoadDictionaryAsync(db.Magic, x => x.Id, "magic entries", cancellationToken);
            MagicType1Table = await LoadDictionaryAsync(db.MagicType1, x => x.Id, "magic type 1 entries", cancellationToken);
            MagicType2Table = await LoadDictionaryAsync(db.MagicType2, x => x.Id, "magic type 2 entries", cancellationToken);
            MagicType3Table = await LoadDictionaryAsync(db.MagicType3, x => x.Id, "magic type 3 entries", cancellationToken);
            MagicType4Table = await LoadDictionaryAsync(db.MagicType4, x => x.Id, "magic type 4 entries", cancellationToken);
            MagicType5Table = await LoadDictionaryAsync(db.MagicType5, x => x.Id, "magic type 5 entries", cancellationToken);
            MagicType6Table = await LoadDictionaryAsync(db.MagicType6, x => x.Id, "magic type 6 entries", cancellationToken);
            MagicType7Table = await LoadDictionaryAsync(db.MagicType7, x => x.Id, "magic type 7 entries", cancellationToken);
            MagicType8Table = await LoadDictionaryAsync(db.MagicType8, x => x.Id, "magic type 8 entries", cancellationToken);
            MagicType9Table = await LoadDictionaryAsync(db.MagicType9, x => x.Id, "magic type 9 entries", cancellationToken);
            SetItemTable = await LoadDictionaryAsync(db.SetItems, x => x.SetIndex, "set item entries", cancellationToken);

            ItemExchangeTable = await LoadDictionaryAsync(db.ItemExchanges, x => x.Index, "item exchange entries", cancellationToken);
            ItemExchangeExpTable = LoadSeedDictionary(new ItemExchangeExpSeed(), x => x.Index, "item exchange extra reward entries");

            var miningExchanges = new MiningExchangeSeed().GetSeedData().ToList();
            MiningExchangesByOreNpc = miningExchanges.ToLookup(x => (x.OreType, x.NpcId));
            logger.LogInformation("Loaded {Count} mining exchange entries", miningExchanges.Count);

            var miningFishingItems = new MiningFishingItemSeed().GetSeedData()
                .Where(x => x.SuccessRate > 0 && x.GiveItemNum > 0)
                .ToList();
            MiningFishingItemsByPool = miningFishingItems.ToLookup(x => (x.Type, x.UseItemType, x.WarStatus));
            logger.LogInformation("Loaded {Count} mining and fishing reward entries", miningFishingItems.Count);

            var eventTriggers = await LoadListAsync(db.EventTriggers, "event trigger entries", cancellationToken);
            EventTriggerTable = eventTriggers.ToDictionary(x => x.Index);
            _eventTriggersByNpc = eventTriggers
                .GroupBy(x => (x.NpcType, x.NpcId))
                .ToDictionary(group => group.Key, group => group.First().TriggerNum);
            NpcItemTable = await LoadDictionaryAsync(db.NpcItems, x => (x.Index, x.IsMonster), "monster drop table entries", cancellationToken);
            var makeItemGroups = await LoadListAsync(db.MakeItemGroups, "make-item-group entries", cancellationToken);
            MakeItemGroupTable = makeItemGroups.ToDictionary(group => group.GroupNum, group => group.GetItems());
            AttendanceRewardTable = await LoadDictionaryAsync(db.AttendanceRewards, x => x.Slot, "attendance reward entries", cancellationToken);
            AchievementTable = await LoadDictionaryAsync(db.Achievements, x => x.Id, "achievements", cancellationToken);
            AchievementTitleTable = await LoadDictionaryAsync(db.AchievementTitles, x => x.Id, "achievement titles", cancellationToken);
            if (NpcItemTable.Count > 0 && !NpcItemTable.Values.Any(row => row.HasAnyDrop))
            {
                logger.LogWarning(
                    "Loaded {Count} monster drop rows but every item ID is zero. Gold/EXP will work, but item bundles cannot spawn until Seed/Data/NpcItems.json is exported from a database with populated K_MONSTER_ITEM rows.",
                    NpcItemTable.Count);
            }

            ItemOpsByItemId = await LoadLookupAsync(db.ItemOps, x => x.ItemId, "item op entries", cancellationToken);
            ServerResourceTable = await LoadDictionaryAsync(db.ServerResources, x => x.ResourceId, x => x.Resource, "server resource entries", cancellationToken);
            PremiumItemTable = await LoadDictionaryAsync(db.PremiumItems, x => x.Type, "premium item entries", cancellationToken);
            PremiumItemExpTable = await LoadListAsync(db.PremiumItemExps, "premium item exp entries", cancellationToken);
            KnightsCapeTable = await LoadDictionaryAsync(db.KnightsCapes, x => x.CapeIndex, "knights cape entries", cancellationToken);
            KingSystemTable = await LoadDictionaryAsync(db.KingSystem, x => x.Nation, "king system entries", cancellationToken);
            MonsterSummonTable = await LoadListAsync(
                db.MonsterSummons.OrderBy(x => x.Index),
                "monster summon entries",
                cancellationToken);
            ZoneInfoTable = await LoadDictionaryAsync(db.ZoneInfos, x => x.ZoneNo, "zone info entries", cancellationToken);
            GameEventsByZone = await LoadLookupAsync(db.GameEvents, x => x.ZoneNum, "game events", cancellationToken);
            CollectionRaceSettingsTable = await LoadDictionaryAsync(db.CollectionRaceSettings, x => x.EventIndex, "collection race settings", cancellationToken);
            CollectionRaceRewardsByEventIndex = await LoadLookupAsync(db.CollectionRaceRewards, x => x.EventIndex, "collection race rewards", cancellationToken);
            SiegeWarfare = await db.SiegeWarfare.AsNoTracking().OrderBy(x => x.CastleIndex).FirstOrDefaultAsync(cancellationToken);
            if (SiegeWarfare != null)
                logger.LogInformation("Loaded siege warfare data (castle owner: clan {ClanId})", SiegeWarfare.MasterKnights);

            IsLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<IReadOnlyList<TEntity>> LoadListAsync<TEntity>(
        IQueryable<TEntity> query,
        string description,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entries = await query.AsNoTracking().ToListAsync(cancellationToken);
        logger.LogInformation("Loaded {Count} {Description}", entries.Count, description);
        return entries;
    }

    private async Task<IReadOnlyDictionary<TKey, TEntity>> LoadDictionaryAsync<TEntity, TKey>(
        IQueryable<TEntity> query,
        Func<TEntity, TKey> keySelector,
        string description,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull
    {
        var entries = await LoadListAsync(query, description, cancellationToken);
        return entries.ToDictionary(keySelector);
    }

    private async Task<IReadOnlyDictionary<TKey, TValue>> LoadDictionaryAsync<TEntity, TKey, TValue>(
        IQueryable<TEntity> query,
        Func<TEntity, TKey> keySelector,
        Func<TEntity, TValue> valueSelector,
        string description,
        CancellationToken cancellationToken)
        where TEntity : class
        where TKey : notnull
    {
        var entries = await LoadListAsync(query, description, cancellationToken);
        return entries.ToDictionary(keySelector, valueSelector);
    }

    private async Task<ILookup<TKey, TEntity>> LoadLookupAsync<TEntity, TKey>(
        IQueryable<TEntity> query,
        Func<TEntity, TKey> keySelector,
        string description,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        var entries = await LoadListAsync(query, description, cancellationToken);
        return entries.ToLookup(keySelector);
    }

    private IReadOnlyDictionary<TKey, TEntity> LoadSeedDictionary<TEntity, TKey>(
        LibreKO.Common.Infrastructure.Persistence.Seed.JsonEntitySeed<TEntity> seed,
        Func<TEntity, TKey> keySelector,
        string description)
        where TEntity : class
        where TKey : notnull
    {
        var entries = seed.GetSeedData().ToList();
        logger.LogInformation("Loaded {Count} {Description}", entries.Count, description);
        return entries.ToDictionary(keySelector);
    }

    private short ResolveSharedMapDataZone(short zoneId, IEnumerable<short> zonesWithData)
    {
        if (!ZoneInfoTable.TryGetValue(zoneId, out var currentZone))
            return zoneId;

        var smdName = currentZone.SmdName?.Trim();
        if (string.IsNullOrEmpty(smdName))
            return zoneId;

        var familyName = NormalizeMapFamily(currentZone.MapName);
        return zonesWithData
            .Where(candidateZoneId => candidateZoneId != zoneId && ZoneInfoTable.ContainsKey(candidateZoneId))
            .Select(candidateZoneId => (ZoneId: candidateZoneId, Zone: ZoneInfoTable[candidateZoneId]))
            .Where(candidate =>
                string.Equals(candidate.Zone.SmdName?.Trim(), smdName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizeMapFamily(candidate.Zone.MapName), familyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.ZoneId)
            .Select(candidate => candidate.ZoneId)
            .FirstOrDefault(zoneId);
    }

    private static string NormalizeMapFamily(string mapName)
    {
        var normalized = (mapName ?? string.Empty).Trim();
        foreach (var suffix in SharedMapVariantSuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return normalized[..^suffix.Length].TrimEnd();
        }

        return normalized;
    }

    private static readonly string[] SharedMapVariantSuffixes =
    [
        " VIII",
        " VII",
        " III",
        " II",
        " IV",
        " VI",
        " IX",
        " V",
        " X",
        " I"
    ];
}
