using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Common.Infrastructure.Persistence.Seed.Entities;

public abstract class LegacyInsertOnlyJsonSeed<T, TKey> : JsonEntitySeed<T>
    where TKey : notnull
{
    protected abstract TKey GetKey(T entity);

    public override IEnumerable<T> GetSeedData()
    {
        var seenKeys = new HashSet<TKey>();
        foreach (var entity in base.GetSeedData())
        {
            if (seenKeys.Add(GetKey(entity)))
            {
                yield return entity;
            }
        }
    }
}

public abstract class SnapshotJsonSeed<T> : JsonEntitySeed<T>
{
    public override bool PerformDelete => true;
}

public class LevelUpSeed : SnapshotJsonSeed<LevelUpData>
{
    protected override string JsonFileName => "LevelUp.json";

    public override IEnumerable<LevelUpData> GetSeedData()
    {
        var seenLevels = new HashSet<byte>();
        foreach (var entry in base.GetSeedData())
        {
            if (seenLevels.Add(entry.Level))
                yield return entry;
        }
    }
}

public class CoefficientSeed : SnapshotJsonSeed<CoefficientData>
{
    protected override string JsonFileName => "Coefficients.json";
}

public class StartPositionSeed : SnapshotJsonSeed<StartPositionData>
{
    protected override string JsonFileName => "StartPositions.json";
}

public class HomeSeed : SnapshotJsonSeed<HomeData>
{
    protected override string JsonFileName => "Homes.json";
}

public class ItemSeed : LegacyInsertOnlyJsonSeed<ItemData, int>
{
    protected override string JsonFileName => "Items.json";

    protected override string ShardPattern => "Items.slot*.json";

    protected override int GetKey(ItemData entity) => entity.Num;
}

public class PusItemSeed : SnapshotJsonSeed<PusItemData>
{
    protected override string JsonFileName => "PusItems.json";
}

public class PusCategorySeed : SnapshotJsonSeed<PusCategoryData>
{
    protected override string JsonFileName => "PusCategories.json";
}

public class WarpSeed : JsonEntitySeed<WarpData>
{
    protected override string JsonFileName => "Warps.json";
}

public class NpcSeed : JsonEntitySeed<NpcData>
{
    protected override string JsonFileName => "Npcs.json";
}

public class NpcPosSeed : JsonEntitySeed<NpcPosData>
{
    protected override string JsonFileName => "NpcPositions.json";
    public override bool PerformDelete => true;

    public override IEnumerable<NpcPosData> GetSeedData()
    {
        var index = 1;
        foreach (var position in base.GetSeedData())
        {
            position.Index = index++;
            yield return position;
        }
    }
}

public class NpcItemSeed : LegacyInsertOnlyJsonSeed<NpcItemData, (short Index, bool IsMonster)>
{
    protected override string JsonFileName => "NpcItems.json";

    public override bool PerformDelete => true;

    protected override (short Index, bool IsMonster) GetKey(NpcItemData entity) =>
        (entity.Index, entity.IsMonster);
}

public class MakeItemGroupSeed : SnapshotJsonSeed<MakeItemGroupData>
{
    protected override string JsonFileName => "MakeItemGroups.json";
}

public class AttendanceRewardSeed : SnapshotJsonSeed<AttendanceRewardData>
{
    protected override string JsonFileName => "AttendanceRewards.json";
}

public class AchievementSeed : SnapshotJsonSeed<AchievementData>
{
    protected override string JsonFileName => "Achievements.json";
}

public class AchievementTitleSeed : SnapshotJsonSeed<AchievementTitleData>
{
    protected override string JsonFileName => "AchievementTitles.json";
}

public class ItemUpgradeSeed : JsonEntitySeed<ItemUpgradeData>
{
    protected override string JsonFileName => "ItemUpgrades.json";
    public override bool PerformDelete => true;
}

public class ItemUpgradeRecipeSeed : SnapshotJsonSeed<ItemUpgradeRecipeData>
{
    protected override string JsonFileName => "ItemUpgradeRecipes.json";
}

public class ItemUpgradeSettingsSeed : SnapshotJsonSeed<ItemUpgradeSettingsData>
{
    protected override string JsonFileName => "ItemUpgradeSettings.json";
}

public class MagicSeed : LegacyInsertOnlyJsonSeed<MagicData, int>
{
    protected override string JsonFileName => "Magic.json";

    protected override int GetKey(MagicData entity) => entity.Id;
}

public class MagicType1Seed : LegacyInsertOnlyJsonSeed<MagicType1Data, int>
{
    protected override string JsonFileName => "MagicType1.json";

    protected override int GetKey(MagicType1Data entity) => entity.Id;
}

public class MagicType2Seed : LegacyInsertOnlyJsonSeed<MagicType2Data, int>
{
    protected override string JsonFileName => "MagicType2.json";

    protected override int GetKey(MagicType2Data entity) => entity.Id;
}

public class MagicType3Seed : LegacyInsertOnlyJsonSeed<MagicType3Data, int>
{
    protected override string JsonFileName => "MagicType3.json";

    protected override int GetKey(MagicType3Data entity) => entity.Id;
}

public class MagicType4Seed : LegacyInsertOnlyJsonSeed<MagicType4Data, int>
{
    protected override string JsonFileName => "MagicType4.json";

    protected override int GetKey(MagicType4Data entity) => entity.Id;
}

public class MagicType5Seed : LegacyInsertOnlyJsonSeed<MagicType5Data, int>
{
    protected override string JsonFileName => "MagicType5.json";

    protected override int GetKey(MagicType5Data entity) => entity.Id;
}

public class MagicType6Seed : LegacyInsertOnlyJsonSeed<MagicType6Data, int>
{
    protected override string JsonFileName => "MagicType6.json";

    protected override int GetKey(MagicType6Data entity) => entity.Id;
}

public class MagicType7Seed : LegacyInsertOnlyJsonSeed<MagicType7Data, int>
{
    protected override string JsonFileName => "MagicType7.json";

    protected override int GetKey(MagicType7Data entity) => entity.Id;
}

public class MagicType8Seed : LegacyInsertOnlyJsonSeed<MagicType8Data, int>
{
    protected override string JsonFileName => "MagicType8.json";

    protected override int GetKey(MagicType8Data entity) => entity.Id;
}

public class MagicType9Seed : LegacyInsertOnlyJsonSeed<MagicType9Data, int>
{
    protected override string JsonFileName => "MagicType9.json";

    protected override int GetKey(MagicType9Data entity) => entity.Id;
}

public class SetItemSeed : SnapshotJsonSeed<SetItemData>
{
    protected override string JsonFileName => "SetItems.json";
}

public class ItemExchangeSeed : SnapshotJsonSeed<ItemExchangeData>
{
    protected override string JsonFileName => "ItemExchanges.json";

    public override IEnumerable<ItemExchangeData> GetSeedData()
    {
        var seenKeys = new HashSet<int>();
        foreach (var entity in base.GetSeedData())
        {
            if (seenKeys.Add(entity.Index))
            {
                yield return entity;
            }
        }
    }
}

public class ItemExchangeExpSeed : SnapshotJsonSeed<ItemExchangeExpData>
{
    protected override string JsonFileName => "ItemExchangeExps.json";
}

public class MiningExchangeSeed : SnapshotJsonSeed<MiningExchangeData>
{
    protected override string JsonFileName => "MiningExchanges.json";
}

public class MiningFishingItemSeed : SnapshotJsonSeed<MiningFishingItemData>
{
    protected override string JsonFileName => "MiningFishingItems.json";

    public override IEnumerable<MiningFishingItemData> GetSeedData()
    {
        var seenIndexes = new HashSet<int>();
        foreach (var entry in base.GetSeedData())
        {
            if (seenIndexes.Add(entry.Index))
                yield return entry;
        }
    }
}

public class EventTriggerSeed : SnapshotJsonSeed<EventTriggerData>
{
    protected override string JsonFileName => "EventTriggers.json";
}

public class ItemOpSeed : SnapshotJsonSeed<ItemOpData>
{
    protected override string JsonFileName => "ItemOps.json";
}

public class ServerResourceSeed : SnapshotJsonSeed<ServerResourceData>
{
    protected override string JsonFileName => "ServerResources.json";
}

public class PremiumItemSeed : SnapshotJsonSeed<PremiumItemData>
{
    protected override string JsonFileName => "PremiumItems.json";
}

public class PremiumItemExpSeed : JsonEntitySeed<PremiumItemExpData>
{
    protected override string JsonFileName => "PremiumItemExps.json";

    public override IEnumerable<PremiumItemExpData> GetSeedData()
    {
        var entries = base.GetSeedData().ToList();
        var usedIndexes = new HashSet<int>();
        var nextIndex = entries.Count == 0 ? 1 : entries.Max(x => x.Index) + 1;

        foreach (var entry in entries)
        {
            if (usedIndexes.Add(entry.Index))
            {
                yield return entry;
                continue;
            }

            entry.Index = nextIndex++;
            usedIndexes.Add(entry.Index);
            yield return entry;
        }
    }
}

public class KnightsCapeSeed : SnapshotJsonSeed<KnightsCapeData>
{
    protected override string JsonFileName => "KnightsCapes.json";
}

public class KingSystemSeed : JsonEntitySeed<KingSystemData>
{
    protected override string JsonFileName => "KingSystem.json";

    // KingSystemData is entirely runtime-mutable (king name, treasury, tax, events).
    // Seed only inserts missing rows; never overwrites existing data.
    public override bool PerformUpdate => false;

    public override IEnumerable<KingSystemData> GetSeedData()
    {
        foreach (var entry in base.GetSeedData())
        {
            entry.KingName ??= string.Empty;
            entry.ImRequestId ??= string.Empty;
            yield return entry;
        }
    }
}

public class MonsterSummonSeed : SnapshotJsonSeed<MonsterSummonData>
{
    protected override string JsonFileName => "MonsterSummons.json";

    public override IEnumerable<MonsterSummonData> GetSeedData()
    {
        var index = 1;
        foreach (var entry in base.GetSeedData())
        {
            entry.Index = index++;
            yield return entry;
        }
    }
}

public class ZoneInfoSeed : SnapshotJsonSeed<ZoneInfoData>
{
    protected override string JsonFileName => "ZoneInfos.json";
}

public class GameEventSeed : SnapshotJsonSeed<GameEventData>
{
    protected override string JsonFileName => "GameEvents.json";
}

public class SiegeWarfareSeed : JsonEntitySeed<SiegeWarfareData>
{
    protected override string JsonFileName => "SiegeWarfare.json";
}

public class CollectionRaceSettingsSeed : SnapshotJsonSeed<CollectionRaceSettingsData>
{
    protected override string JsonFileName => "CollectionRaceSettings.json";
}

public class CollectionRaceRewardSeed : SnapshotJsonSeed<CollectionRaceRewardData>
{
    protected override string JsonFileName => "CollectionRaceRewards.json";
}
