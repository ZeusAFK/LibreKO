using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class PetBagTests
{
    private const int AutomaticLooting = 700_012_000;
    private const int HpScroll = 978_011_000;
    private const int HpScrollKind = 177;
    private const int SwordSlot = 1;

    private static readonly ItemData.Item Looting = new() { Kind = PetBag.AutomaticLootingKind, Slot = PetBag.ItemSlotCode };
    private static readonly ItemData.Item Scroll = new() { Kind = HpScrollKind, Slot = PetBag.ItemSlotCode };
    private static readonly ItemData.Item Sword = new() { Kind = 21, Slot = SwordSlot };

    private static ItemData.Item? Lookup(int id) => id switch
    {
        AutomaticLooting => Looting,
        HpScroll => Scroll,
        _ => null,
    };

    [Fact]
    public void TheBagTakesFamiliarItemsOneOfEachKind()
    {
        var bag = new ItemSlot[PetSheet.InventorySize];
        bag[0] = new ItemSlot { ItemId = AutomaticLooting, Count = 1 };

        Assert.True(PetBag.Fits(bag, 1, Scroll, Lookup));
        Assert.False(PetBag.Fits(bag, 1, Looting, Lookup));
        Assert.True(PetBag.Fits(bag, 0, Looting, Lookup));
        Assert.False(PetBag.Fits(bag, 1, Sword, Lookup));
    }

    [Fact]
    public void LootingNeedsAutomaticLootingInTheBag()
    {
        var bag = new ItemSlot[PetSheet.InventorySize];
        Assert.False(PetBag.Loots(bag, Lookup));

        bag[2] = new ItemSlot { ItemId = AutomaticLooting, Count = 1 };
        Assert.True(PetBag.Loots(bag, Lookup));
    }
}
