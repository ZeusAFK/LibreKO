using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class IconVeilTests
{
    private const int AnyItem = 120050000;
    private const long Now = 1_800_000_000;

    private static ItemSlot Slot(ItemFlag flag, short durability = 100) =>
        new() { ItemId = AnyItem, Count = 1, Durability = durability, Flag = (byte)flag };

    [Theory]
    [InlineData(ItemFlag.Unsealed, IconWash.None)]
    [InlineData(ItemFlag.Rented, IconWash.Rented)]
    [InlineData(ItemFlag.CharacterSeal, IconWash.Rented)]
    [InlineData(ItemFlag.Duplicate, IconWash.Duplicate)]
    [InlineData(ItemFlag.Sealed, IconWash.Sealed)]
    [InlineData(ItemFlag.SealedPremium, IconWash.Sealed)]
    [InlineData(ItemFlag.NotBound, IconWash.None)]
    [InlineData(ItemFlag.Bound, IconWash.None)]
    public void EachItemStateGetsItsWash(ItemFlag flag, IconWash wash)
    {
        Assert.Equal(wash, IconVeil.Wash(Slot(flag), Now));
    }

    [Fact]
    public void AnExhaustedItemIsRedWhateverItsState()
    {
        Assert.Equal(IconWash.Exhausted, IconVeil.Wash(Slot(ItemFlag.Sealed, durability: 0), Now));
        Assert.Equal(IconWash.Exhausted, IconVeil.Wash(Slot(ItemFlag.Unsealed, durability: 0), Now));
    }

    [Fact]
    public void AnItemIsRedFromTheSecondItRunsOut()
    {
        var timed = Slot(ItemFlag.Sealed);
        timed.ExpiresAt = (int)Now;

        Assert.Equal(IconWash.Expired, IconVeil.Wash(timed, Now));
        Assert.Equal(IconWash.Sealed, IconVeil.Wash(timed, Now - 1));
        Assert.Equal(IconWash.Sealed, IconVeil.Wash(Slot(ItemFlag.Sealed), Now));
    }

    [Fact]
    public void AClearedSlotForgetsItsExpiry()
    {
        var timed = Slot(ItemFlag.Unsealed);
        timed.ExpiresAt = (int)Now;

        timed.Clear();
        timed.ItemId = AnyItem;
        timed.Durability = 100;

        Assert.Equal(IconWash.None, IconVeil.Wash(timed, Now));
    }

    [Fact]
    public void AnEmptySlotHasNoWash()
    {
        Assert.Equal(IconWash.None, IconVeil.Wash(default, Now));
    }

    [Theory]
    [InlineData(ItemFlag.Unsealed, true, IconBadge.NotBound)]
    [InlineData(ItemFlag.Unsealed, false, IconBadge.None)]
    [InlineData(ItemFlag.NotBound, false, IconBadge.NotBound)]
    [InlineData(ItemFlag.Bound, true, IconBadge.Bound)]
    [InlineData(ItemFlag.Sealed, true, IconBadge.None)]
    public void BindOnEquipItemsShowTheBindBadges(ItemFlag flag, bool bindsOnEquip, IconBadge badge)
    {
        Assert.Equal(badge, IconVeil.Badge(IconVeil.ShownFlag(flag, bindsOnEquip)));
    }
}
