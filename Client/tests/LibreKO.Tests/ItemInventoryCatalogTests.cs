using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

[CollectionDefinition("Item inventory catalog", DisableParallelization = true)]
public class ItemInventoryCatalogCollection;

[Collection("Item inventory catalog")]
public class ItemInventoryCatalogTests
{
    private const int FavorsId = 389014000;
    private const int FavorsVariant = FavorsId + 1;
    private const int FavorsSibling = FavorsId + 2;
    private const int SilverBarId = 379067000;
    private const int MissingId = 379068000;
    private const int DarkVaneId = 119101000;
    private const int DarkVaneVariant = 119101207;
    private const int DarkVaneSibling = 119101208;
    private const int DarkVaneCategory = 23;
    private const int LegacyDarkVaneWeight = 20;
    private const int ServerDarkVaneWeight = 30;
    private const int DarkVaneIcon = 11121000;
    private const int DarkVaneUpgrade = 7;
    private const int LegacyWeight = 22;
    private const int ServerWeight = 45;
    private const int ServerBarWeight = 10;
    private const int LegacyBarWeight = 1;
    private const int SingleItemRoom = 1;
    private const int Stackable = 1;
    private const int SeparateItem = 0;
    private const int UnitPrice = 4900;
    private const int Wallet = 40_515_957;
    private const int CarriedWeight = 1326;
    private const int Capacity = 11_300;
    private const int RequestedCount = 400;
    private const int LegacyMaximum = 453;
    private const int ServerMaximum = 221;
    private const int FavorsSkill = 490014;
    private const int FavorsIcon = 38901400;
    private const int MaximumLevel = 100;
    private const int ParallelReaders = 32;

    private static string Catalog(string entries, int schema = ItemInventoryCatalog.SchemaVersion) =>
        $$"""{"schemaVersion": {{schema}}, "items": { {{entries}} } }""";

    private static string Definition(int itemId, int weight, int countable) =>
        $"\"{itemId}\":[{weight},{countable}]";

    [Fact]
    public void TheCatalogStoresExactItemWeightAndStackRules()
    {
        var definitions = ItemInventoryCatalog.Parse(Catalog(Definition(FavorsId, ServerWeight, Stackable)));
        Assert.Equal(new ItemInventoryDefinition(ServerWeight, Stackable), definitions[FavorsId]);
        Assert.False(definitions.ContainsKey(FavorsVariant));
    }

    [Fact]
    public void TheMaximumServerWeightIsAccepted()
    {
        var definitions = ItemInventoryCatalog.Parse(Catalog(Definition(FavorsId, short.MaxValue, Stackable)));
        Assert.Equal(short.MaxValue, definitions[FavorsId].Weight);
    }

    [Fact]
    public void WeightsBeyondTheServerFieldAreRejected() =>
        Assert.Throws<JsonException>(() => ItemInventoryCatalog.Parse(Catalog(Definition(FavorsId, short.MaxValue + 1, Stackable))));

    [Theory]
    [InlineData(2)]
    [InlineData(byte.MaxValue)]
    public void NonBooleanCountableValuesRetainTheServerByteRange(int countable)
    {
        var definitions = ItemInventoryCatalog.Parse(Catalog(Definition(FavorsId, ServerWeight, countable)));
        Assert.Equal(countable, definitions[FavorsId].Countable);
    }

    [Fact]
    public void TheFavorsPurchaseLimitUsesTheServerWeight()
    {
        using var catalog = new InventoryCatalog();
        Assert.Equal(LegacyMaximum, Limit(ItemData.Get(FavorsId)!));
        catalog.Set(FavorsId, ServerWeight, Stackable);
        Assert.Equal(ServerMaximum, Limit(ItemData.Get(FavorsId)!));
        Assert.True((long)UnitPrice * RequestedCount <= Wallet);
        Assert.True((long)ServerWeight * ServerMaximum <= Capacity - CarriedWeight);
        Assert.True((long)ServerWeight * (ServerMaximum + 1) > Capacity - CarriedWeight);
    }

    [Fact]
    public void FourHundredFavorsExceedCapacityEvenInAnEmptyInventory()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(FavorsId, ServerWeight, Stackable);
        var item = ItemData.Get(FavorsId)!;
        Assert.True(BuyLimit.Max(Wallet, UnitPrice, Capacity, item.Weight, Inventory.StackMax) < RequestedCount);
        Assert.True((long)item.Weight * RequestedCount > Capacity);
    }

    [Fact]
    public void ZeroOverridesReplacePositiveLegacyMetadata()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(FavorsId, 0, SeparateItem);
        var item = ItemData.Get(FavorsId)!;
        Assert.Equal(0, item.Weight);
        Assert.Equal(SeparateItem, item.Countable);
        Assert.Equal(SingleItemRoom, BuyLimit.Max(Wallet, UnitPrice, 0, item.Weight, item.Countable == SeparateItem ? SingleItemRoom : Inventory.StackMax));
    }

    [Fact]
    public void BarsUseTheServerNonStackableRule()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(SilverBarId, ServerBarWeight, SeparateItem);
        Assert.Equal(SeparateItem, ItemData.Get(SilverBarId)!.Countable);
        Assert.Equal(ServerBarWeight, ItemData.Get(SilverBarId)!.Weight);
        Assert.Equal(Stackable, catalog.Legacy(SilverBarId).Countable);
    }

    [Fact]
    public void SearchResultsUseTheServerBarMetadataFromALegacyBaseDefinition()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(SilverBarId, ServerBarWeight, SeparateItem);
        var legacy = catalog.Legacy(SilverBarId);
        var hit = new ItemSearchHit(legacy, null);
        Assert.Equal(SilverBarId, hit.Id);
        Assert.Equal(SilverBarId, hit.Def.Id);
        Assert.Equal(ServerBarWeight, hit.Def.Weight);
        Assert.Equal(SeparateItem, hit.Def.Countable);
        Assert.Equal("Silver bar", hit.Family);
        Assert.Equal(0, hit.Plus);
        Assert.Equal(legacy.Name, hit.Def.Name);
        Assert.Equal(legacy.Icon, hit.Def.Icon);
        Assert.Equal(Stackable, legacy.Countable);
    }

    [Fact]
    public void SearchVariantsResolveExactMetadataWithoutChangingTheirIdentity()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(DarkVaneVariant, ServerDarkVaneWeight, SeparateItem);
        var legacy = catalog.Legacy(DarkVaneId);
        var extension = catalog.DarkVaneExtension();
        var hit = new ItemSearchHit(legacy, extension);
        Assert.Equal(DarkVaneVariant, hit.Id);
        Assert.Equal(DarkVaneId, hit.Def.Id);
        Assert.Equal(ServerDarkVaneWeight, hit.Def.Weight);
        Assert.Equal(SeparateItem, hit.Def.Countable);
        Assert.Equal("Dark Vane", hit.Def.Name);
        Assert.Equal(DarkVaneIcon, hit.Def.Icon);
        Assert.Equal("Dark Vane", hit.Family);
        Assert.Equal(DarkVaneUpgrade, hit.Plus);
        Assert.Same(extension, hit.Ext);
        Assert.Equal(LegacyDarkVaneWeight, legacy.Weight);
        Assert.Equal(LegacyDarkVaneWeight, ItemData.Get(DarkVaneSibling)!.Weight);
        Assert.Same(legacy, ItemData.Get(DarkVaneSibling));
    }

    [Fact]
    public void ExactVariantOverridesDoNotAlterTheBaseOrSibling()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(FavorsVariant, ServerWeight, SeparateItem);
        var variant = ItemData.Get(FavorsVariant)!;
        Assert.Equal(FavorsId, variant.Id);
        Assert.Equal(ServerWeight, variant.Weight);
        Assert.Equal(SeparateItem, variant.Countable);
        Assert.Equal(LegacyWeight, ItemData.Get(FavorsId)!.Weight);
        Assert.Equal(Stackable, ItemData.Get(FavorsSibling)!.Countable);
        Assert.Same(catalog.Legacy(FavorsId), ItemData.Get(FavorsSibling));
        Assert.NotSame(catalog.Legacy(FavorsId), variant);
    }

    [Fact]
    public void ABaseOverrideDoesNotBecomeASiblingOverride()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(FavorsId, ServerWeight, SeparateItem);
        Assert.Equal(ServerWeight, ItemData.Get(FavorsId)!.Weight);
        Assert.Equal(LegacyWeight, ItemData.Get(FavorsVariant)!.Weight);
        Assert.Equal(Stackable, ItemData.Get(FavorsVariant)!.Countable);
    }

    [Fact]
    public void DistinctVariantsRetainIndependentCachedMetadata()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(FavorsVariant, ServerWeight, SeparateItem);
        catalog.Set(FavorsSibling, ServerBarWeight, Stackable);
        var variant = ItemData.Get(FavorsVariant)!;
        var sibling = ItemData.Get(FavorsSibling)!;
        Assert.Equal(ServerWeight, variant.Weight);
        Assert.Equal(ServerBarWeight, sibling.Weight);
        Assert.Same(variant, ItemData.Get(FavorsVariant));
        Assert.Same(sibling, ItemData.Get(FavorsSibling));
        Assert.NotSame(variant, sibling);
    }

    [Fact]
    public void MetadataOverridesPreserveVisualAndGameplayIdentity()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(FavorsVariant, ServerWeight, SeparateItem);
        var item = ItemData.Get(FavorsVariant)!;
        Assert.Equal(FavorsId, item.Id);
        Assert.Equal("Water of favors", item.Name);
        Assert.Equal(FavorsIcon, item.Icon);
        Assert.Equal(FavorsSkill, item.Effect1);
        Assert.Equal(MaximumLevel, item.ReqLevelMax);
        Assert.Equal(UnitPrice, item.BuyPrice);
        Assert.Equal(LegacyWeight, catalog.Legacy(FavorsId).Weight);
        Assert.Equal(Stackable, catalog.Legacy(FavorsId).Countable);
    }

    [Fact]
    public void MissingCatalogRowsKeepTheLegacyDefinition()
    {
        using var catalog = new InventoryCatalog();
        Assert.Same(catalog.Legacy(FavorsId), ItemData.Get(FavorsId));
        catalog.Set(MissingId, ServerWeight, Stackable);
        Assert.Null(ItemData.Get(MissingId));
    }

    [Fact]
    public void ParallelReadersShareThePublishedVariantDefinition()
    {
        using var catalog = new InventoryCatalog();
        catalog.Set(FavorsVariant, ServerWeight, SeparateItem);
        var definitions = new ItemData.Item?[ParallelReaders];
        Parallel.For(0, ParallelReaders, index => definitions[index] = ItemData.Get(FavorsVariant));
        Assert.All(definitions, item => Assert.Same(definitions[0], item));
        Assert.Equal(ServerWeight, definitions[0]!.Weight);
    }

    [Fact]
    public void UnknownSchemasAndDuplicateIdsAreRejected()
    {
        Assert.Throws<JsonException>(() => ItemInventoryCatalog.Parse(Catalog("", ItemInventoryCatalog.SchemaVersion + 1)));
        string entry = Definition(FavorsId, ServerWeight, Stackable);
        Assert.Throws<JsonException>(() => ItemInventoryCatalog.Parse(Catalog($"{entry},{entry}")));
    }

    [Fact]
    public void InvalidTrailingRowsRejectTheEntireCatalog()
    {
        string valid = Definition(FavorsId, ServerWeight, Stackable);
        Assert.Throws<JsonException>(() => ItemInventoryCatalog.Parse(Catalog($"{valid},\"{SilverBarId}\":[-1,0]")));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[1,0,0]")]
    [InlineData("[-1,0]")]
    [InlineData("[1,-1]")]
    [InlineData("[1,256]")]
    [InlineData("[2147483648,0]")]
    [InlineData("[1.5,0]")]
    [InlineData("[null,0]")]
    [InlineData("[1,true]")]
    [InlineData("[\"1\",0]")]
    public void MalformedDefinitionsAreRejected(string values) =>
        Assert.Throws<JsonException>(() => ItemInventoryCatalog.Parse(Catalog($"\"{FavorsId}\":{values}")));

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1\",\"items\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"items\":[]}")]
    public void MalformedCatalogsAreRejected(string json) =>
        Assert.Throws<JsonException>(() => ItemInventoryCatalog.Parse(json));

    private static int Limit(ItemData.Item item) =>
        BuyLimit.Max(Wallet, UnitPrice, Capacity - CarriedWeight, item.Weight, Inventory.StackMax);

    private sealed class InventoryCatalog : IDisposable
    {
        private readonly FieldInfo _loaded = Field("_loaded");
        private readonly bool _wasLoaded;
        private readonly Dictionary<int, ItemData.Item> _items = Cache<ItemData.Item>("_items");
        private readonly Dictionary<int, ItemInventoryDefinition> _definitions = Cache<ItemInventoryDefinition>("_inventoryDefinitions");
        private readonly Dictionary<long, ItemData.Ext> _extensions =
            (Dictionary<long, ItemData.Ext>)Field("_exts").GetValue(null)!;
        private readonly ConcurrentDictionary<int, ItemData.Item> _resolved =
            (ConcurrentDictionary<int, ItemData.Item>)Field("_inventoryItems").GetValue(null)!;
        private readonly Dictionary<int, ItemData.Item> _oldItems;
        private readonly Dictionary<int, ItemInventoryDefinition> _oldDefinitions;
        private readonly Dictionary<int, ItemData.Item> _oldResolved;
        private readonly Dictionary<long, ItemData.Ext> _oldExtensions;

        public InventoryCatalog()
        {
            _wasLoaded = (bool)_loaded.GetValue(null)!;
            _oldItems = new(_items);
            _oldDefinitions = new(_definitions);
            _oldResolved = new(_resolved);
            _oldExtensions = new(_extensions);
            _extensions.Clear();
            _items.Clear();
            _definitions.Clear();
            _resolved.Clear();
            _items[FavorsId] = new ItemData.Item
            {
                Id = FavorsId, Name = "Water of favors", Weight = LegacyWeight, Countable = Stackable,
                BuyPrice = UnitPrice, Effect1 = FavorsSkill, Icon = FavorsIcon, ReqLevelMax = MaximumLevel,
            };
            _items[SilverBarId] = new ItemData.Item
            { Id = SilverBarId, Name = "Silver bar", Weight = LegacyBarWeight, Countable = Stackable };
            _items[DarkVaneId] = new ItemData.Item
            { Id = DarkVaneId, Name = "Dark Vane", Cat = DarkVaneCategory, Weight = LegacyDarkVaneWeight, Countable = SeparateItem, Icon = DarkVaneIcon };
            _loaded.SetValue(null, true);
        }

        public void Set(int id, int weight, int countable) =>
            _definitions[id] = new ItemInventoryDefinition(weight, countable);

        public ItemData.Item Legacy(int id) => _items[id];

        public ItemData.Ext DarkVaneExtension()
        {
            int id = ItemData.ExtIdFor(DarkVaneVariant);
            var extension = new ItemData.Ext
            { Id = id, Cat = DarkVaneCategory, Linked = DarkVaneId, Name = "Dark Vane(+7)", MagicOrRare = ItemData.Rarity.Unique };
            _extensions[((long)DarkVaneCategory << 32) | (uint)id] = extension;
            return extension;
        }

        public void Dispose()
        {
            Restore(_items, _oldItems);
            Restore(_definitions, _oldDefinitions);
            _resolved.Clear();
            foreach (var row in _oldResolved) _resolved[row.Key] = row.Value;
            _extensions.Clear();
            foreach (var row in _oldExtensions) _extensions[row.Key] = row.Value;
            _loaded.SetValue(null, _wasLoaded);
        }

        private static FieldInfo Field(string name) => typeof(ItemData).GetField(
            name, BindingFlags.Static | BindingFlags.NonPublic)!;

        private static Dictionary<int, T> Cache<T>(string name) =>
            (Dictionary<int, T>)Field(name).GetValue(null)!;

        private static void Restore<T>(Dictionary<int, T> target, Dictionary<int, T> original)
        {
            target.Clear();
            foreach (var row in original) target[row.Key] = row.Value;
        }
    }
}
