using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol;
using Xunit;

namespace LibreKO.Game.Tests;

public class PowerUpStoreCatalogTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PusItemData Scroll = new() { Id = 1, ItemId = 800079000, Price = 100, Category = 1 };

    private static PusDiscountData Discount(int price, DateTime startsAt, int hours, int pusItemId = 1, int id = 1) =>
        new() { Id = id, PusItemId = pusItemId, Price = price, StartsAt = startsAt, Duration = hours };

    [Fact]
    public void AnActiveDiscountLowersThePrice_AndSaysWhenItEnds()
    {
        var price = PowerUpStoreCatalog.PriceOf(Scroll, [Discount(75, Now.AddHours(-1), 5)], Now);

        price.Price.Should().Be(75);
        price.BasePrice.Should().Be(100);
        price.DiscountEndsAt.Should().Be(Now.AddHours(4));
    }

    [Fact]
    public void ADiscountBeforeItsStartOrAfterItsDurationIsIgnored()
    {
        PowerUpStoreCatalog.PriceOf(Scroll, [Discount(75, Now.AddHours(1), 5)], Now).Price.Should().Be(100);
        PowerUpStoreCatalog.PriceOf(Scroll, [Discount(75, Now.AddHours(-6), 5)], Now).Price.Should().Be(100);
    }

    [Fact]
    public void AZeroDurationNeverEnds()
    {
        var price = PowerUpStoreCatalog.PriceOf(Scroll, [Discount(80, Now.AddYears(-1), PusDiscountData.NoEnd)], Now);

        price.Price.Should().Be(80);
        price.DiscountEndsAt.Should().BeNull();
    }

    [Fact]
    public void TheCheapestActiveDiscountWins_AndOtherItemsDiscountsDoNotApply()
    {
        var discounts = new[]
        {
            Discount(90, Now.AddHours(-1), 0, id: 1),
            Discount(70, Now.AddHours(-1), 0, id: 2),
            Discount(10, Now.AddHours(-1), 0, pusItemId: 2, id: 3),
        };

        PowerUpStoreCatalog.PriceOf(Scroll, discounts, Now).Price.Should().Be(70);
    }

    [Fact]
    public void ADiscountThatIsNotCheaperIsIgnored() =>
        PowerUpStoreCatalog.PriceOf(Scroll, [Discount(120, Now.AddHours(-1), 0)], Now).Price.Should().Be(100);

    [Fact]
    public void SeedProblemsNameStoreItemsMissingFromTheItemTable_AndDiscountsForMissingStoreItems()
    {
        var items = new[] { Scroll, new PusItemData { Id = 2, ItemId = 810039000, Price = 100, Category = 2 } };
        var discounts = new[] { Discount(75, Now, 0, pusItemId: 1, id: 1), Discount(75, Now, 0, pusItemId: 9, id: 2) };

        var problems = PowerUpStoreCatalog.SeedProblems(items, discounts, itemId => itemId != 810039000);

        problems.Should().HaveCount(2);
        problems[0].Should().Contain("810039000");
        problems[1].Should().Contain("discount 2").And.Contain("9");
    }
}
