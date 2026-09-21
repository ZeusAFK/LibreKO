using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Common.Domain.Services;

public interface IGameDataService
{
    IReadOnlyDictionary<byte, long> LevelUpTable { get; }
    IReadOnlyDictionary<short, CoefficientData> CoefficientTable { get; }
    IReadOnlyDictionary<short, StartPositionData> StartPositionTable { get; }
    IReadOnlyDictionary<int, ItemData> ItemTable { get; }
    IReadOnlyDictionary<int, NpcData> NpcTable { get; }
    IReadOnlyDictionary<int, NpcData> MonsterTable { get; }
    IReadOnlyList<NpcPosData> NpcPositions { get; }
    IReadOnlyDictionary<int, ItemUpgradeData> UpgradeTable { get; }
    ILookup<int, ItemUpgradeRecipeData> UpgradeRecipesByOrigin { get; }
    IReadOnlyList<ItemUpgradeSettingsData> UpgradeSettings { get; }
    IReadOnlyDictionary<int, MagicData> MagicTable { get; }
    IReadOnlyDictionary<int, MagicType1Data> MagicType1Table { get; }
    IReadOnlyDictionary<int, MagicType2Data> MagicType2Table { get; }
    IReadOnlyDictionary<int, MagicType3Data> MagicType3Table { get; }
    IReadOnlyDictionary<int, MagicType4Data> MagicType4Table { get; }
    IReadOnlyDictionary<int, MagicType5Data> MagicType5Table { get; }
    IReadOnlyDictionary<int, MagicType6Data> MagicType6Table { get; }
    IReadOnlyDictionary<int, MagicType7Data> MagicType7Table { get; }
    IReadOnlyDictionary<int, MagicType8Data> MagicType8Table { get; }
    IReadOnlyDictionary<int, MagicType9Data> MagicType9Table { get; }
    IReadOnlyDictionary<int, SetItemData> SetItemTable { get; }
    IReadOnlyDictionary<int, ItemExchangeData> ItemExchangeTable { get; }
    IReadOnlyDictionary<int, ItemExchangeExpData> ItemExchangeExpTable { get; }
    ILookup<(byte OreType, short NpcId), MiningExchangeData> MiningExchangesByOreNpc { get; }
    ILookup<(GatherType Type, GatherTool Tool, GatherWarStatus War), MiningFishingItemData> MiningFishingItemsByPool { get; }
    IReadOnlyDictionary<int, EventTriggerData> EventTriggerTable { get; }
    IReadOnlyDictionary<(short Index, bool IsMonster), NpcItemData> NpcItemTable { get; }
    IReadOnlyDictionary<int, int[]> MakeItemGroupTable { get; }
    ILookup<int, ItemOpData> ItemOpsByItemId { get; }
    IReadOnlyDictionary<int, string> ServerResourceTable { get; }
    IReadOnlyDictionary<byte, PremiumItemData> PremiumItemTable { get; }
    IReadOnlyList<PremiumItemExpData> PremiumItemExpTable { get; }
    IReadOnlyDictionary<short, KnightsCapeData> KnightsCapeTable { get; }
    IReadOnlyDictionary<byte, KingSystemData> KingSystemTable { get; }
    IReadOnlyList<MonsterSummonData> MonsterSummonTable { get; }
    IReadOnlyDictionary<short, ZoneInfoData> ZoneInfoTable { get; }
    ILookup<byte, GameEventData> GameEventsByZone { get; }
    IReadOnlyDictionary<int, AttendanceRewardData> AttendanceRewardTable { get; }
    IReadOnlyDictionary<int, AchievementData> AchievementTable { get; }
    IReadOnlyDictionary<int, AchievementTitleData> AchievementTitleTable { get; }
    IReadOnlyDictionary<int, CollectionRaceSettingsData> CollectionRaceSettingsTable { get; }
    ILookup<int, CollectionRaceRewardData> CollectionRaceRewardsByEventIndex { get; }
    SiegeWarfareData? SiegeWarfare { get; }
    bool IsLoaded { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);
    Task ReloadAsync(CancellationToken cancellationToken = default);

    long GetMaxExpForLevel(byte level);
    CoefficientData? GetCoefficient(short classId);
    StartPositionData? GetStartPosition(short zoneId);
    ItemData? GetItem(int itemId);
    NpcData? GetNpc(int npcId);
    NpcData? GetNpc(int npcId, bool isMonster);
    NpcData? GetSpawnProto(NpcPosData pos);
    ItemUpgradeData? GetUpgrade(int index);
    ItemUpgradeRecipeData? GetUpgradeRecipe(int originItemId, int requiredItemId);
    ItemUpgradeSettingsData? GetUpgradeSetting(short itemType, short grade, int firstMaterial, int secondMaterial);
    MagicData? GetMagic(int magicId);
    SetItemData? GetSetItem(int setIndex);
    ItemExchangeData? GetItemExchange(int index);
    ItemExchangeExpData? GetItemExchangeExp(int index);
    int GetEventTrigger(short npcType, short trapNumber);
    NpcItemData? GetNpcItem(short index, bool isMonster);
    IEnumerable<ItemOpData> GetItemOps(int itemId);
    string? GetServerResource(int resourceId);

    int GetPremiumProperty(byte premiumType, PremiumPropertyType property);
}

public enum PremiumPropertyType
{
    Noah = 1,
    Drop = 2,
    RepairDiscount = 4,
    ItemSell = 5
}
