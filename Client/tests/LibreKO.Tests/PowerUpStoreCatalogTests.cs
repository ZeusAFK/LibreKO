using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PowerUpStoreCatalogTests
{
    private static readonly PowerUpStoreEntry Scroll = new(1, 800079000, "HP Scroll", "", 1, 300);
    private static readonly PowerUpStoreEntry Armor = new(2, 800077000, "Armor Scroll", "", 1, 100, Featured: true);
    private static readonly PowerUpStoreEntry Package = new(3, 810039000, "VIP Package", "", 2, 200, Featured: true);
    private static readonly PowerUpStoreEntry Potion = new(4, 810192000, "Potion of Crisis", "", 1, 400, DiscountPrice: 50);
    private static readonly PowerUpStoreEntry[] Catalogue = [Scroll, Armor, Package, Potion];

    private static int[] Ids(IEnumerable<PowerUpStoreEntry> entries) => entries.Select(e => e.Id).ToArray();

    [Fact]
    public void TheViewShowsTheSelectedCategoryByNameByDefault() =>
        Assert.Equal([2, 1, 4], Ids(PowerUpStoreCatalog.View(Catalogue, 1, "", PowerUpStoreCatalog.DefaultSort)));

    [Fact]
    public void TheFeaturedSectionGathersFeaturedItemsFromEveryCategory() =>
        Assert.Equal([2, 3], Ids(PowerUpStoreCatalog.View(Catalogue, PowerUpStoreCatalog.FeaturedCategory, "", PowerUpStoreSort.NameAToZ)));

    [Fact]
    public void PriceSortsUseTheDiscountedPrice()
    {
        Assert.Equal([4, 2, 1], Ids(PowerUpStoreCatalog.View(Catalogue, 1, "", PowerUpStoreSort.PriceLowToHigh)));
        Assert.Equal([1, 2, 4], Ids(PowerUpStoreCatalog.View(Catalogue, 1, "", PowerUpStoreSort.PriceHighToLow)));
    }

    [Fact]
    public void NamesSortBothWays()
    {
        Assert.Equal([2, 1, 4], Ids(PowerUpStoreCatalog.View(Catalogue, 1, "", PowerUpStoreSort.NameAToZ)));
        Assert.Equal([4, 1, 2], Ids(PowerUpStoreCatalog.View(Catalogue, 1, "", PowerUpStoreSort.NameZToA)));
    }

    [Fact]
    public void ASearchLooksThroughEveryCategoryIgnoringCase() =>
        Assert.Equal([3], Ids(PowerUpStoreCatalog.View(Catalogue, 1, "  vip ", PowerUpStoreSort.NameAToZ)));

    [Fact]
    public void ASearchWithNoMatchIsEmpty() =>
        Assert.Empty(PowerUpStoreCatalog.View(Catalogue, 1, "sword", PowerUpStoreSort.NameAToZ));

    [Fact]
    public void EverySortHasALabel() =>
        Assert.All(PowerUpStoreCatalog.Sorts, sort => Assert.False(string.IsNullOrWhiteSpace(PowerUpStoreCatalog.Label(sort))));

    [Fact]
    public void ACategoryHasADiscountWhenOneOfItsItemsIsDiscounted()
    {
        Assert.True(PowerUpStoreCatalog.HasDiscount(Catalogue, 1));
        Assert.False(PowerUpStoreCatalog.HasDiscount(Catalogue, 2));
        Assert.False(PowerUpStoreCatalog.HasDiscount(Catalogue, PowerUpStoreCatalog.FeaturedCategory));
    }

    [Fact]
    public void ADiscountOnlyCountsWhenItIsCheaper()
    {
        Assert.Equal(50, Potion.Price);
        Assert.True(Potion.Discounted);
        var notCheaper = Scroll with { DiscountPrice = 300 };
        Assert.False(notCheaper.Discounted);
        Assert.Equal(300, notCheaper.Price);
    }
}
