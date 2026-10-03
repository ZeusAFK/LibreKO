using System.Collections.Generic;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ShopCatalogueTests
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [100] = "Water of Ibexs", [200] = "Spell of life", [300] = "Prayer of grace", [400] = "Prayer of love",
    };

    private static ShopCatalogue Catalogue() => new(new[]
    {
        new ShopCatalogue.Entry(300, 1, 0),
        new ShopCatalogue.Entry(100, 0, 2),
        new ShopCatalogue.Entry(200, 0, 0),
        new ShopCatalogue.Entry(400, 1, 5),
    }, id => Names[id]);

    [Fact]
    public void PagesAreOrderedAndItemsSitInTheirCells()
    {
        var shop = Catalogue();
        Assert.Equal(new[] { 0, 1 }, shop.Pages);
        var first = shop.Page(0);
        Assert.Equal(ShopCatalogue.PageSize, first.Length);
        Assert.Equal(200, first[0]);
        Assert.Equal(100, first[2]);
        Assert.Equal(0, first[1]);
    }

    [Fact]
    public void SearchMatchesPartOfTheNameInPageOrder()
    {
        Assert.Equal(new[] { 300, 400 }, Catalogue().Search("PRAYER"));
    }

    [Fact]
    public void SearchMatchesAnExactItemNumber()
    {
        Assert.Equal(new[] { 200 }, Catalogue().Search("200"));
    }

    [Fact]
    public void SearchWithNoMatchReturnsNothing()
    {
        Assert.Empty(Catalogue().Search("zzz"));
        Assert.Empty(Catalogue().Search("   "));
    }

    [Fact]
    public void ASingleOrEmptyGroupHasAtMostOnePage()
    {
        Assert.Empty(ShopCatalogue.Empty.Pages);
        var single = new ShopCatalogue(new[] { new ShopCatalogue.Entry(100, 3, 1) }, id => Names[id]);
        Assert.Equal(new[] { 3 }, single.Pages);
    }

    [Fact]
    public void SlicePagesTheResults()
    {
        var items = new List<int>();
        for (int i = 1; i <= 30; i++) items.Add(i);
        Assert.Equal(2, ShopCatalogue.PagesFor(items.Count));
        Assert.Equal(1, ShopCatalogue.PagesFor(0));
        var second = ShopCatalogue.Slice(items, 1);
        Assert.Equal(25, second[0]);
        Assert.Equal(30, second[5]);
        Assert.Equal(0, second[6]);
    }
}
