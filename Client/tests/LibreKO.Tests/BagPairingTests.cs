using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BagPairingTests
{
    private sealed class Window;

    private readonly Window shop = new();
    private readonly Window storage = new();
    private readonly BagPairing<Window> pairing = new();

    [Fact]
    public void AnInventoryOpenedForTheWindowClosesWithIt()
    {
        pairing.Attach(shop, inventoryWasOpen: false);
        Assert.True(pairing.Detach(shop));
    }

    [Fact]
    public void AnInventoryThatWasOpenStaysOpen()
    {
        pairing.Attach(shop, inventoryWasOpen: true);
        Assert.False(pairing.Detach(shop));
    }

    [Fact]
    public void AnInventoryThePlayerTouchedStaysOpen()
    {
        pairing.Attach(shop, inventoryWasOpen: false);
        pairing.PlayerTouched();
        Assert.False(pairing.Detach(shop));
    }

    [Fact]
    public void ASecondWindowReplacesTheFirstAndKeepsTheInventoryItOpened()
    {
        Assert.Null(pairing.Attach(storage, inventoryWasOpen: false));
        Assert.Same(storage, pairing.Attach(shop, inventoryWasOpen: true));
        Assert.True(pairing.Detach(shop));
    }

    [Fact]
    public void TheSameWindowAgainKeepsTheInventoryItOpened()
    {
        pairing.Attach(shop, inventoryWasOpen: false);
        Assert.Null(pairing.Attach(shop, inventoryWasOpen: true));
        Assert.True(pairing.Detach(shop));
    }

    [Fact]
    public void ClosingAReplacedWindowLeavesTheCurrentOneAlone()
    {
        pairing.Attach(storage, inventoryWasOpen: false);
        pairing.Attach(shop, inventoryWasOpen: true);
        Assert.False(pairing.Detach(storage));
        Assert.True(pairing.Detach(shop));
    }
}
