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
    [InlineData(810433000, true)]
    [InlineData(WarehouseRules.NonStorableIdFirst, false)]
    [InlineData(WarehouseRules.NonStorableIdLast, false)]
    [InlineData(WarehouseRules.NonStorableIdLast + 1, true)]
    public void VaultStorableRefusesOnlyTheNonStorableIdRange(int itemId, bool storable)
    {
        Assert.Equal(storable, WarehouseRules.VaultStorable(itemId));
    }
}
