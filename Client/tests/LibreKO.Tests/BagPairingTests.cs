using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BagPairingTests
{
    private sealed class Window;

    private readonly Window _shop = new();
    private readonly Window _storage = new();
    private readonly BagPairing<Window> _pairing = new();

    [Fact]
    public void AnInventoryOpenedForTheWindowClosesWithIt()
    {
        _pairing.Attach(_shop, inventoryWasOpen: false);
        Assert.True(_pairing.Detach(_shop));
    }

    [Fact]
    public void AnInventoryThatWasOpenStaysOpen()
    {
        _pairing.Attach(_shop, inventoryWasOpen: true);
        Assert.False(_pairing.Detach(_shop));
    }

    [Fact]
    public void AnInventoryThePlayerTouchedStaysOpen()
    {
        _pairing.Attach(_shop, inventoryWasOpen: false);
        _pairing.PlayerTouched();
        Assert.False(_pairing.Detach(_shop));
    }

    [Fact]
    public void ASecondWindowReplacesTheFirstAndKeepsTheInventoryItOpened()
    {
        Assert.Null(_pairing.Attach(_storage, inventoryWasOpen: false));
        Assert.Same(_storage, _pairing.Attach(_shop, inventoryWasOpen: true));
        Assert.True(_pairing.Detach(_shop));
    }

    [Fact]
    public void TheSameWindowAgainKeepsTheInventoryItOpened()
    {
        _pairing.Attach(_shop, inventoryWasOpen: false);
        Assert.Null(_pairing.Attach(_shop, inventoryWasOpen: true));
        Assert.True(_pairing.Detach(_shop));
    }

    [Fact]
    public void ClosingAReplacedWindowLeavesTheCurrentOneAlone()
    {
        _pairing.Attach(_storage, inventoryWasOpen: false);
        _pairing.Attach(_shop, inventoryWasOpen: true);
        Assert.False(_pairing.Detach(_storage));
        Assert.True(_pairing.Detach(_shop));
    }
}
