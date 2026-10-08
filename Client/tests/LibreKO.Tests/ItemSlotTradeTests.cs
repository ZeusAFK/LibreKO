using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ItemSlotTradeTests
{
    [Theory]
    [InlineData(ItemFlag.Unsealed, true)]
    [InlineData(ItemFlag.NotBound, true)]
    [InlineData(ItemFlag.Rented, false)]
    [InlineData(ItemFlag.CharacterSeal, false)]
    [InlineData(ItemFlag.Duplicate, false)]
    [InlineData(ItemFlag.Sealed, false)]
    [InlineData(ItemFlag.Bound, false)]
    public void SellingUsesTheServersItemStateRestrictions(ItemFlag state, bool allowed)
    {
        Assert.Equal(allowed, new ItemSlot { ItemId = 100, Count = 1, Flag = (byte)state }.IsTradable);
    }

    [Fact]
    public void ALinkedItemCannotBeSoldEvenWhenUnsealed()
    {
        Assert.False(new ItemSlot { ItemId = 100, Count = 1, UniqueId = 23 }.IsTradable);
    }
}
