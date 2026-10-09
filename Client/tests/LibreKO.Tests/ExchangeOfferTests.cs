using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ExchangeOfferTests
{
    private const int Arrow = 391010000;
    private const int Potion = 389010000;
    private const int Sword = 110110001;

    private const int AnyRace = 0;
    private const int LinkedUniqueId = 23;

    private static bool Countable(int itemId) => itemId is Arrow or Potion;

    private static ItemSlot Held(int itemId, ItemFlag state = ItemFlag.Unsealed, int uniqueId = 0) =>
        new() { ItemId = itemId, Count = 1, Flag = (byte)state, UniqueId = uniqueId };

    private static ItemData.Item Def(int itemId, int race = AnyRace) => new() { Id = itemId, Race = race };

    [Fact]
    public void AnOrdinaryTradableItemCanBeOffered()
    {
        Assert.True(ExchangeOffer.IsOfferable(Held(Sword), Def(Sword)));
    }

    [Theory]
    [InlineData(ItemData.NoTradeIdFirst)]
    [InlineData(ItemData.NoTradeIdLast)]
    public void NoTradeIdsAreRefusedLikeTheServer(int itemId)
    {
        Assert.False(ExchangeOffer.IsOfferable(Held(itemId), Def(itemId)));
    }

    [Fact]
    public void QuestItemsAreRefusedLikeTheServer()
    {
        Assert.False(ExchangeOffer.IsOfferable(Held(Sword), Def(Sword, ItemData.QuestItemRace)));
    }

    [Fact]
    public void AnItemWithoutDefinitionIsRefusedLikeTheServer()
    {
        Assert.False(ExchangeOffer.IsOfferable(Held(Sword), null));
    }

    [Theory]
    [InlineData(ItemFlag.Rented)]
    [InlineData(ItemFlag.CharacterSeal)]
    [InlineData(ItemFlag.Duplicate)]
    [InlineData(ItemFlag.Sealed)]
    [InlineData(ItemFlag.Bound)]
    public void ItemStatesTheServerRefusesCannotBeOffered(ItemFlag state)
    {
        Assert.False(ExchangeOffer.IsOfferable(Held(Sword, state), Def(Sword)));
    }

    [Fact]
    public void ALinkedItemCannotBeOffered()
    {
        Assert.False(ExchangeOffer.IsOfferable(Held(Sword, uniqueId: LinkedUniqueId), Def(Sword)));
    }

    [Fact]
    public void RepeatedCountableOffersShareOneSlotLikeTheServerStack()
    {
        Assert.Equal(2, ExchangeOffer.SlotsUsed(new[] { Arrow, Arrow, Sword }, Countable));
        Assert.Equal(3, ExchangeOffer.SlotsUsed(new[] { Arrow, Sword, Sword }, Countable));
    }

    [Fact]
    public void AFullOfferStillAcceptsMoreOfAnOfferedCountable()
    {
        var offer = Enumerable.Repeat(Sword, ExchangeOffer.ItemSlots - 1).Append(Arrow).ToArray();
        Assert.True(ExchangeOffer.HasRoomFor(offer, Arrow, Countable));
        Assert.False(ExchangeOffer.HasRoomFor(offer, Potion, Countable));
        Assert.False(ExchangeOffer.HasRoomFor(offer, Sword, Countable));
    }

    [Fact]
    public void AnOfferBelowTheVisibleSlotsAcceptsAnyItem()
    {
        var offer = Enumerable.Repeat(Arrow, ExchangeOffer.ItemSlots + 3).ToArray();
        Assert.True(ExchangeOffer.HasRoomFor(offer, Sword, Countable));
    }

    [Theory]
    [InlineData("1", 50, true, 1)]
    [InlineData(" 50 ", 50, true, 50)]
    [InlineData("51", 50, false, 0)]
    [InlineData("0", 50, false, 0)]
    [InlineData("-3", 50, false, 0)]
    [InlineData("1,000", 5000, false, 0)]
    [InlineData("", 50, false, 0)]
    [InlineData("abc", 50, false, 0)]
    [InlineData("99999999999", 50, false, 0)]
    public void TypedAmountsOutsideTheOfferedStackAreRejectedInsteadOfClamped(string typed, int max, bool valid, int expected)
    {
        Assert.Equal(valid, ExchangeOffer.TryParseAmount(typed, max, out int amount));
        if (valid) Assert.Equal(expected, amount);
    }
}
