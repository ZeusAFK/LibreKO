using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class WarehouseRulesTests
{
    [Theory]
    [InlineData(156210000, 0, true)]
    [InlineData(810433000, WarehouseRules.NonStorableRace, false)]
    [InlineData(1113331000, WarehouseRules.VaultTicketRaceFirst, false)]
    [InlineData(930540000, WarehouseRules.VaultTicketRaceLast, false)]
    [InlineData(WarehouseRules.NonStorableIdFirst, 0, false)]
    [InlineData(WarehouseRules.NonStorableIdLast, 0, false)]
    [InlineData(WarehouseRules.NonStorableIdFirst - 1, 0, true)]
    [InlineData(1_000_000_000, 0, true)]
    public void StorableFollowsTheRetailWarehouseRule(int itemId, int race, bool storable)
    {
        Assert.Equal(storable, WarehouseRules.Storable(itemId, race));
    }

    [Theory]
    [InlineData(810433000, false)]
    [InlineData(WarehouseRules.NonStorableIdFirst, false)]
    [InlineData(WarehouseRules.NoTradeIdFirst, true)]
    [InlineData(WarehouseRules.NoTradeIdLast, true)]
    [InlineData(WarehouseRules.NoTradeIdLast + 1, false)]
    public void IsNoTradeIdMatchesTheRangeTheVipAndClanVaultsRefuse(int itemId, bool noTrade)
    {
        Assert.Equal(noTrade, WarehouseRules.IsNoTradeId(itemId));
    }
}
