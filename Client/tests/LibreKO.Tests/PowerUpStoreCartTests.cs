using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PowerUpStoreCartTests
{
    private static readonly PowerUpStoreEntry Scroll = new(1, 800079000, "HP Scroll", "", 1, 300);
    private static readonly PowerUpStoreEntry Package = new(3, 810039000, "VIP Package", "", 2, 200);

    [Fact]
    public void AddingTheSameEntryTwiceRaisesItsCount()
    {
        var cart = new PowerUpStoreCart();
        cart.Add(Scroll);
        cart.Add(Scroll);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(2, line.Count);
    }

    [Fact]
    public void TheTotalIsEveryLinesPriceTimesItsCount()
    {
        var cart = new PowerUpStoreCart();
        cart.Add(Scroll, 2);
        cart.Add(Package, 3);
        Assert.Equal(1_200, cart.Total);
        Assert.Equal(5, cart.Units);
    }

    [Fact]
    public void ADiscountedLineCostsTheDiscountedPrice()
    {
        var cart = new PowerUpStoreCart();
        cart.Add(Scroll with { DiscountPrice = 250 }, 2);
        Assert.Equal(500, cart.Total);
    }

    [Fact]
    public void DecreasingStopsAtOne_RemovingDropsTheLine()
    {
        var cart = new PowerUpStoreCart();
        cart.Add(Scroll);
        cart.Decrease(Scroll.Id);
        Assert.Equal(1, cart.Lines[0].Count);
        cart.Remove(Scroll.Id);
        Assert.True(cart.IsEmpty);
    }

    [Fact]
    public void ALineNeverPassesItsCountLimit()
    {
        var cart = new PowerUpStoreCart();
        cart.Add(Scroll, PowerUpStoreCart.LineCountMax);
        Assert.False(cart.CanIncrease(Scroll.Id));
        cart.Increase(Scroll.Id);
        cart.Add(Scroll);
        Assert.Equal(PowerUpStoreCart.LineCountMax, cart.Lines[0].Count);
    }

    [Fact]
    public void TheCartTakesAnyNumberOfDifferentItems()
    {
        var cart = new PowerUpStoreCart();
        for (var id = 1; id <= 40; id++)
            cart.Add(Scroll with { Id = id });
        Assert.Equal(40, cart.Lines.Count);
    }

    [Fact]
    public void TheCartIsAffordableUpToTheBalance()
    {
        var cart = new PowerUpStoreCart();
        cart.Add(Scroll, 2);
        Assert.True(cart.Affordable(600));
        Assert.False(cart.Affordable(599));
    }

    [Fact]
    public void RepricingTakesTheNewPrices_AndDropsWhatIsNoLongerSold()
    {
        var cart = new PowerUpStoreCart();
        cart.Add(Scroll with { DiscountPrice = 250 }, 2);
        cart.Add(Package);

        var changed = cart.Reprice([Scroll]);

        Assert.True(changed);
        var line = Assert.Single(cart.Lines);
        Assert.Equal(600, line.Total);
        Assert.False(cart.Reprice([Scroll]));
    }
}
