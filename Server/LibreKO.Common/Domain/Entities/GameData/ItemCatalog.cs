using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace LibreKO.Common.Domain.Entities.GameData;

public sealed class ItemCatalog : IReadOnlyDictionary<int, ItemData>
{
    public const int BaseSpan = 1000;
    public const int BlockSpan = 1_000_000_000;
    private const int PlusSpan = 100;
    private const int UpgradeLevelSpan = 10;
    private const int TopUpgradeLevel = 10;

    private readonly Dictionary<int, ItemBaseData> bases;
    private readonly Dictionary<(byte VariantSet, int Id), ItemVariantData> variants;
    private readonly ILookup<byte, ItemVariantData> variantsBySet;
    private readonly ConcurrentDictionary<int, ItemData> composed = new();

    public ItemCatalog(IEnumerable<ItemBaseData> bases, IEnumerable<ItemVariantData> variants)
    {
        this.bases = bases.ToDictionary(item => item.Id);
        var variantList = variants.ToList();
        this.variants = variantList.ToDictionary(variant => (variant.VariantSet, variant.Id));
        variantsBySet = variantList.ToLookup(variant => variant.VariantSet);
        Count = Ids().Count();
    }

    public int Count { get; }

    public IEnumerable<int> Keys => Ids();

    public IEnumerable<ItemData> Values => Ids().Select(id => Compose(id)!);

    public ItemData this[int key] => TryGetValue(key, out var item) ? item : throw new KeyNotFoundException($"No item {key}");

    public static int BaseOf(int itemId) => itemId / BaseSpan * BaseSpan;

    public static int VariantIdOf(int itemId) => itemId / BlockSpan * BaseSpan + itemId % BaseSpan;

    public static int ItemIdOf(ItemBaseData item, ItemVariantData variant) => item.Id + variant.Id % BaseSpan;

    public static bool Fits(ItemBaseData item, ItemVariantData variant) =>
        item.Id % BaseSpan == 0
        && variant.VariantSet == item.VariantSet
        && variant.Id % BaseSpan != 0
        && variant.Id / BaseSpan == item.Id / BlockSpan
        && (variant.MadeFor == 0 || variant.MadeFor == item.Id);

    public bool ContainsKey(int key) => TryGetValue(key, out _);

    public bool TryGetValue(int key, [MaybeNullWhen(false)] out ItemData value)
    {
        if (composed.TryGetValue(key, out value))
            return true;
        var item = Compose(key);
        if (item == null)
            return false;
        value = composed.GetOrAdd(key, item);
        return true;
    }

    public IEnumerator<KeyValuePair<int, ItemData>> GetEnumerator() =>
        Ids().Select(id => new KeyValuePair<int, ItemData>(id, Compose(id)!)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<int> Ids()
    {
        foreach (var item in bases.Values)
        {
            yield return item.Id;
            foreach (var variant in variantsBySet[item.VariantSet])
                if (Fits(item, variant))
                    yield return ItemIdOf(item, variant);
        }
    }

    private ItemData? Compose(int itemId)
    {
        if (bases.TryGetValue(itemId, out var exact))
            return Compose(itemId, exact, null);
        if (!bases.TryGetValue(BaseOf(itemId), out var item))
            return null;
        return variants.TryGetValue((item.VariantSet, VariantIdOf(itemId)), out var variant) && Fits(item, variant)
            ? Compose(itemId, item, variant)
            : null;
    }

    public static ItemData Compose(int itemId, ItemBaseData item, ItemVariantData? variant) => new()
    {
        Num = itemId,
        Name = NameOf(item, variant),
        Kind = item.Kind,
        Slot = item.Slot,
        Race = item.Race,
        Class = item.Class,
        Damage = Short(item.Damage + (variant?.Damage ?? 0)),
        Delay = item.Delay,
        Range = item.Range,
        Weight = item.Weight,
        Duration = Short(item.Duration + (variant?.Duration ?? 0)),
        BuyPrice = (int)Math.Min((long)item.BuyPrice * Math.Max((short)1, variant?.PriceMultiplier ?? 1), int.MaxValue),
        SaleType = item.SaleType,
        Ac = Short(item.Ac + (variant?.Ac ?? 0)),
        Countable = item.Countable,
        Effect1 = item.Effect1,
        Effect2 = item.Effect2,
        ReqLevel = (byte)Math.Clamp(item.ReqLevel + (variant?.ReqLevel ?? 0), byte.MinValue, byte.MaxValue),
        ReqLevelMax = item.ReqLevelMax,
        ReqRank = item.ReqRank,
        ReqTitle = variant is { ReqTitle: > 0 } ? (byte)variant.ReqTitle : item.ReqTitle,
        ReqStr = Short(item.ReqStr + (variant?.ReqStr ?? 0)),
        ReqSta = Short(item.ReqSta + (variant?.ReqSta ?? 0)),
        ReqDex = Short(item.ReqDex + (variant?.ReqDex ?? 0)),
        ReqIntel = Short(item.ReqIntel + (variant?.ReqIntel ?? 0)),
        ReqCha = Short(item.ReqCha + (variant?.ReqCha ?? 0)),
        Rarity = variant?.Rarity ?? ItemRarity.Regular,
        Hitrate = variant?.Hitrate ?? 0,
        Evasionrate = variant?.Evasionrate ?? 0,
        FireDamage = variant?.FireDamage ?? 0,
        IceDamage = variant?.IceDamage ?? 0,
        LightningDamage = variant?.LightningDamage ?? 0,
        PoisonDamage = variant?.PoisonDamage ?? 0,
        HpDrain = variant?.HpDrain ?? 0,
        MpDamage = variant?.MpDamage ?? 0,
        MpDrain = variant?.MpDrain ?? 0,
        MirrorDamage = variant?.MirrorDamage ?? 0,
        MaxHpB = variant?.MaxHp ?? 0,
        MaxMpB = variant?.MaxMp ?? 0,
        StrB = variant?.Str ?? 0,
        StaB = variant?.Sta ?? 0,
        DexB = variant?.Dex ?? 0,
        IntelB = variant?.Intel ?? 0,
        ChaB = variant?.Cha ?? 0,
        FireR = variant?.FireR ?? 0,
        ColdR = variant?.ColdR ?? 0,
        LightningR = variant?.LightningR ?? 0,
        MagicR = variant?.MagicR ?? 0,
        PoisonR = variant?.PoisonR ?? 0,
        CurseR = variant?.CurseR ?? 0,
        SellingGroup = item.SellingGroup,
        DaggerAc = variant?.DaggerAc ?? 0,
        JamadarAc = variant?.JamadarAc ?? 0,
        SwordAc = variant?.SwordAc ?? 0,
        AxeAc = variant?.AxeAc ?? 0,
        MaceAc = variant?.MaceAc ?? 0,
        SpearAc = variant?.SpearAc ?? 0,
        BowAc = variant?.BowAc ?? 0,
        UnbindStones = Short(item.UnbindStones + (variant?.UnbindStones ?? 0)),
        Grade = (short)(itemId % UpgradeLevelSpan),
        UpgradeLevel = UpgradeLevel(variant),
        ItemClass = item.ItemClass,
        GradeClass = Short(item.ItemClass + (variant?.GradeClass ?? 0)),
    };

    public static string NameOf(ItemBaseData item, ItemVariantData? variant)
    {
        if (variant != null && ItemRarity.NamesTheItem(variant.Rarity) && variant.Name.Length > 0)
            return variant.Name;
        var level = UpgradeLevel(variant);
        return level > 0 ? $"{item.Name}(+{level})" : item.Name;
    }

    public static int UpgradeLevel(ItemVariantData? variant)
    {
        if (variant == null || !ItemRarity.TakesUpgradeLevel(variant.Rarity))
            return 0;
        if (variant.Rarity == ItemRarity.Reverse)
            return variant.Plus % PlusSpan;
        var tail = variant.Id % UpgradeLevelSpan;
        if (tail != 0)
            return tail;
        return variant.Rarity != ItemRarity.Regular ? TopUpgradeLevel : 0;
    }

    private static short Short(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
}
