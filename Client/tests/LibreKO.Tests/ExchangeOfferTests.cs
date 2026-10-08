using System.Linq;
using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ExchangeOfferTests
{
    private const int Arrow = 391010000;
    private const int Potion = 389010000;
    private const int Sword = 110110001;

    private static bool Countable(int itemId) => itemId is Arrow or Potion;

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
