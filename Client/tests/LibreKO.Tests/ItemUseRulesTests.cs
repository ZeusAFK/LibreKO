using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ItemUseRulesTests
{
    private const int Sword = 156211000;
    private const int NoTradeId = 900_100_000;
    private const int QuestRace = 20;
    private const int NoStoreRace = 73;
    private const int NoSaleRace = 12;
    private const int NoTradeDestroyRace = 70;
    private const int NoTradeDestroyAltRace = 71;

    [Fact]
    public void PlainItemsGoEverywhere()
    {
        foreach (ItemUse use in System.Enum.GetValues<ItemUse>())
            Assert.Equal(ItemUseRules.Allowed, ItemUseRules.Refusal(use, Sword, 0, 0));
    }

    [Fact]
    public void SealedAndBoundItemsSayWhyTheyCannotBeTraded()
    {
        Assert.Equal(ItemUseRules.SealedNoTradeText, ItemUseRules.Refusal(ItemUse.Trade, Sword, 0, (byte)ItemFlag.Sealed));
        Assert.Equal(ItemUseRules.BoundNoTradeText, ItemUseRules.Refusal(ItemUse.Trade, Sword, 0, (byte)ItemFlag.Bound));
    }

    [Fact]
    public void QuestItemsCannotBeTradedOrSold()
    {
        Assert.Equal(ItemUseRules.CannotTradeText, ItemUseRules.Refusal(ItemUse.Trade, Sword, QuestRace, 0));
        Assert.Equal(ItemUseRules.NonTradableText, ItemUseRules.Refusal(ItemUse.NpcSell, Sword, QuestRace, 0));
    }

    [Fact]
    public void ShopsRefuseHeldBackStatesAndSomeRaces()
    {
        Assert.Equal(ItemUseRules.NonTradableText, ItemUseRules.Refusal(ItemUse.NpcSell, Sword, 0, (byte)ItemFlag.Rented));
        Assert.Equal(ItemUseRules.NonTradableText, ItemUseRules.Refusal(ItemUse.NpcSell, Sword, NoSaleRace, 0));
        Assert.Equal(ItemUseRules.NonTradableText, ItemUseRules.Refusal(ItemUse.NpcSell, NoTradeId, 0, 0));
        Assert.Equal(ItemUseRules.Allowed, ItemUseRules.Refusal(ItemUse.NpcSell, Sword, 0, (byte)ItemFlag.NotBound));
    }

    [Fact]
    public void StorageFollowsTheWarehouseRule()
    {
        Assert.Equal(ItemUseRules.NonStorableText, ItemUseRules.Refusal(ItemUse.Warehouse, Sword, NoStoreRace, 0));
        Assert.Equal(ItemUseRules.Allowed, ItemUseRules.Refusal(ItemUse.Warehouse, Sword, 0, (byte)ItemFlag.Bound));
    }

    [Fact]
    public void TheClanBankKeepsBoundAndTimedItemsOut()
    {
        Assert.Equal(ItemUseRules.NonStorableText, ItemUseRules.Refusal(ItemUse.ClanBank, Sword, 0, (byte)ItemFlag.Bound));
        Assert.Equal(ItemUseRules.NonStorableText, ItemUseRules.Refusal(ItemUse.ClanBank, Sword, 0, 0, timed: true));
    }

    [Theory]
    [InlineData(QuestRace)]
    [InlineData(NoTradeDestroyRace)]
    [InlineData(NoTradeDestroyAltRace)]
    public void TheClanBankKeepsOutWhatCannotBeTraded(int race)
    {
        Assert.Equal(ItemUseRules.NonStorableText, ItemUseRules.Refusal(ItemUse.ClanBank, Sword, race, 0));
        Assert.Equal(ItemUseRules.CannotTradeText, ItemUseRules.Refusal(ItemUse.Trade, Sword, race, 0));
        Assert.Equal(ItemUseRules.Allowed, ItemUseRules.Refusal(ItemUse.Warehouse, Sword, race, 0));
    }
}
