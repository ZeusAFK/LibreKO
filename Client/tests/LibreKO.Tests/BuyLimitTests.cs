using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class BuyLimitTests
{
    private const int Room = 9999;

    [Fact]
    public void TheWalletLimitsTheCount() => Assert.Equal(3, BuyLimit.Max(1000, 300, null, 1, Room));

    [Fact]
    public void FreeWeightLimitsTheCount() => Assert.Equal(2, BuyLimit.Max(1_000_000, 10, 50, 20, Room));

    [Fact]
    public void BagRoomLimitsTheCount() => Assert.Equal(5, BuyLimit.Max(1_000_000, 10, null, 1, 5));

    [Fact]
    public void AFreeWeightlessItemIsLimitedByRoomOnly() => Assert.Equal(Room, BuyLimit.Max(0, 0, 0, 0, Room));

    [Fact]
    public void NothingAffordableOrCarriableIsZero()
    {
        Assert.Equal(0, BuyLimit.Max(100, 300, null, 1, Room));
        Assert.Equal(0, BuyLimit.Max(1000, 10, -40, 5, Room));
    }
}
