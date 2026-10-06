using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class MerchantSearchTests
{
    private static MerchantSearchRow Row(string item, int price, int type = MerchantSearch.SellingType, int seller = 1) =>
        new(seller, $"Seller{seller}", item.Length, price, type, item);

    private static MerchantSearch Filled()
    {
        var search = new MerchantSearch();
        search.Add(new[]
        {
            Row("Iron Sword", 500),
            Row("Holy water", 70),
            Row("Iron Shield", 900),
            Row("Holy water", 60, MerchantSearch.BuyingType),
        });
        return search;
    }

    [Fact]
    public void TheScopeShowsOneKindOfStall()
    {
        var search = Filled();

        Assert.Equal(3, search.Count);
        search.SetType(MerchantSearch.BuyingType);
        Assert.Equal(60, Assert.Single(search.Visible()).Price);
    }

    [Fact]
    public void SearchMatchesPartOfTheNameIgnoringCase()
    {
        var search = Filled();

        search.Search(" iron ");

        Assert.Equal(new[] { "Iron Sword", "Iron Shield" }, search.Visible().Select(row => row.ItemName));
        search.ShowAll();
        Assert.Equal(3, search.Count);
    }

    [Fact]
    public void SortsByPriceEitherWayAndByName()
    {
        var search = Filled();

        search.SortBy(MerchantSearchSort.PriceLowToHigh);
        Assert.Equal(new[] { 70, 500, 900 }, search.Visible().Select(row => row.Price));
        search.SortBy(MerchantSearchSort.PriceHighToLow);
        Assert.Equal(new[] { 900, 500, 70 }, search.Visible().Select(row => row.Price));
        search.SortBy(MerchantSearchSort.Name);
        Assert.Equal(new[] { "Holy water", "Iron Shield", "Iron Sword" }, search.Visible().Select(row => row.ItemName));
    }

    [Fact]
    public void TenRowsAPageAndEightPagesAGroup()
    {
        var search = new MerchantSearch();
        search.Add(Enumerable.Range(0, 95).Select(i => Row($"Item {i:00}", i)));

        Assert.Equal(10, search.PageCount);
        Assert.Equal(Enumerable.Range(0, 8), search.GroupPages());
        search.GoTo(9);
        Assert.Equal(5, search.Visible().Count);
        Assert.Equal(new[] { 8, 9 }, search.GroupPages());
        search.Next();
        Assert.Equal(9, search.Page);
    }

    [Fact]
    public void EmptySlotsAreNotRows()
    {
        var search = new MerchantSearch();

        search.Add(new[] { new MerchantSearchRow(1, "Seller", 0, 0, MerchantSearch.SellingType, "") });

        Assert.Equal(0, search.Count);
    }

    [Fact]
    public void OnlyTheOfficialListSkillOpensTheSearch()
    {
        Assert.True(MerchantSearch.OpensWith(500126));
        Assert.False(MerchantSearch.OpensWith(490076));
    }
}
