using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class InventoryTests
{
    private const int OneHand = 0, RightOnly = 1, LeftOnly = 2, TwoHandRight = 3, TwoHandLeft = 4;
    private const int Ear = 10, Ring = 12;
    private const int NoFallback = -1;

    private static ItemSlot Item(int id, int count = 1, int durability = 100) =>
        new() { ItemId = id, Count = (short)count, Durability = (short)durability };

    private static Inventory Empty()
    {
        var inv = new Inventory();
        inv.EnsureLength(Inventory.GridStart + Inventory.GridCount);
        return inv;
    }

    private static Inventory BagFullExcept(params int[] freeAbs)
    {
        var inv = Empty();
        for (int abs = Inventory.GridStart; abs < Inventory.GridStart + Inventory.GridCount; abs++)
            inv[abs] = Item(900_000_000 + abs);
        foreach (int abs in freeAbs) inv[abs] = default;
        return inv;
    }

    [Fact]
    public void GridLayout_MatchesTheWireConstants_NotALocalCopy()
    {
        Assert.Equal(InventoryConstants.InventoryStart, Inventory.GridStart);
        Assert.Equal(InventoryConstants.HaveMax, Inventory.GridCount);
    }

    [Fact]
    public void SlotClassification_SplitsWornGearFromTheBag()
    {
        var inv = Empty();
        Assert.True(inv.IsEquipSlot(InventoryConstants.RightHand));
        Assert.False(inv.IsGridSlot(InventoryConstants.RightHand));
        Assert.True(inv.IsGridSlot(Inventory.GridStart));
        Assert.False(inv.IsEquipSlot(Inventory.GridStart));
    }

    [Fact]
    public void Reset_CopiesTheSeed_SoThePacketBufferIsNotAliased()
    {
        var seed = new[] { Item(1), Item(2) };
        var inv = new Inventory();
        inv.Reset(seed);

        seed[0] = Item(999);                         // mutate the caller's array afterwards

        Assert.Equal(1, inv[0].ItemId);
    }

    [Fact]
    public void Reset_WithNull_EmptiesTheInventory()
    {
        var inv = Empty();
        inv.Reset(null);
        Assert.Equal(0, inv.Length);
    }

    [Fact]
    public void EnsureLength_GrowsButNeverShrinks()
    {
        var inv = new Inventory();
        inv.EnsureLength(20);
        Assert.Equal(20, inv.Length);
        inv.EnsureLength(5);
        Assert.Equal(20, inv.Length);
    }

    [Fact]
    public void FirstFreeGridSlot_FindsTheLowestEmptyBagSlot()
    {
        var inv = BagFullExcept(Inventory.GridStart + 7, Inventory.GridStart + 3);
        Assert.Equal(Inventory.GridStart + 3, inv.FirstFreeGridSlot());
    }

    [Fact]
    public void FirstFreeGridSlot_ReturnsMinusOneWhenTheBagIsFull()
    {
        Assert.Equal(-1, BagFullExcept().FirstFreeGridSlot());
    }

    [Fact]
    public void FirstFreeGridSlot_IgnoresEmptyWornSlots()
    {
        var inv = BagFullExcept();
        inv[InventoryConstants.Head] = default;      // worn slot free, bag still full
        Assert.Equal(-1, inv.FirstFreeGridSlot());
    }

    [Fact]
    public void FreeGridSlots_ListsThemInOrder()
    {
        var inv = BagFullExcept(Inventory.GridStart + 9, Inventory.GridStart + 1);
        Assert.Equal(new[] { Inventory.GridStart + 1, Inventory.GridStart + 9 }, inv.FreeGridSlots());
    }

    [Fact]
    public void ResolveEquipDest_OneHander_PrefersTheRightHand_ThenTheLeft()
    {
        var inv = Empty();
        Assert.Equal(InventoryConstants.RightHand, inv.ResolveEquipDest(OneHand, NoFallback));

        inv[InventoryConstants.RightHand] = Item(1);
        Assert.Equal(InventoryConstants.LeftHand, inv.ResolveEquipDest(OneHand, NoFallback));
    }

    [Fact]
    public void ResolveEquipDest_OneHander_ReplacesTheRightHandWhenBothAreFull()
    {
        var inv = Empty();
        inv[InventoryConstants.RightHand] = Item(1);
        inv[InventoryConstants.LeftHand] = Item(2);

        Assert.Equal(InventoryConstants.RightHand, inv.ResolveEquipDest(OneHand, NoFallback));
    }

    [Theory]
    [InlineData(RightOnly, InventoryConstants.RightHand)]
    [InlineData(TwoHandRight, InventoryConstants.RightHand)]
    [InlineData(LeftOnly, InventoryConstants.LeftHand)]
    [InlineData(TwoHandLeft, InventoryConstants.LeftHand)]
    public void ResolveEquipDest_HandedTypes_HaveAFixedHome(int slotType, int expected)
    {
        var inv = Empty();
        inv[expected] = Item(1);                     // occupied: still goes there
        Assert.Equal(expected, inv.ResolveEquipDest(slotType, NoFallback));
    }

    [Fact]
    public void ResolveEquipDest_Earrings_PreferRightThenLeft()
    {
        var inv = Empty();
        Assert.Equal(InventoryConstants.RightEar, inv.ResolveEquipDest(Ear, NoFallback));

        inv[InventoryConstants.RightEar] = Item(1);
        Assert.Equal(InventoryConstants.LeftEar, inv.ResolveEquipDest(Ear, NoFallback));
    }

    [Fact]
    public void ResolveEquipDest_Rings_PreferRightThenLeft()
    {
        var inv = Empty();
        Assert.Equal(InventoryConstants.RightRing, inv.ResolveEquipDest(Ring, NoFallback));

        inv[InventoryConstants.RightRing] = Item(1);
        Assert.Equal(InventoryConstants.LeftRing, inv.ResolveEquipDest(Ring, NoFallback));
    }

    [Fact]
    public void ResolveEquipDest_UnknownType_UsesTheFallback()
    {
        Assert.Equal(InventoryConstants.Head, Empty().ResolveEquipDest(99, InventoryConstants.Head));
    }

    [Theory]
    [InlineData(TwoHandRight)]
    [InlineData(TwoHandLeft)]
    public void HandsToClear_EquippingATwoHander_FreesTheOtherHand(int slotType)
    {
        var inv = Empty();
        inv[InventoryConstants.LeftHand] = Item(50);          // a shield

        var clear = inv.HandsToClear(InventoryConstants.RightHand, slotType, _ => false);

        Assert.Equal(new[] { InventoryConstants.LeftHand }, clear);
    }

    [Fact]
    public void HandsToClear_EquippingAOneHander_FreesAnExistingTwoHander()
    {
        var inv = Empty();
        inv[InventoryConstants.RightHand] = Item(77);         // a two-hander already worn

        var clear = inv.HandsToClear(InventoryConstants.LeftHand, OneHand, id => id == 77);

        Assert.Equal(new[] { InventoryConstants.RightHand }, clear);
    }

    [Fact]
    public void HandsToClear_OneHanderBesideAOneHander_ClearsNothing()
    {
        var inv = Empty();
        inv[InventoryConstants.RightHand] = Item(50);

        Assert.Empty(inv.HandsToClear(InventoryConstants.LeftHand, OneHand, _ => false));
    }

    [Fact]
    public void HandsToClear_EmptyOtherHand_ClearsNothing()
    {
        Assert.Empty(Empty().HandsToClear(InventoryConstants.RightHand, TwoHandRight, _ => true));
    }

    [Fact]
    public void HandsToClear_NonHandDestination_NeverConflicts()
    {
        var inv = Empty();
        inv[InventoryConstants.RightHand] = Item(77);

        Assert.Empty(inv.HandsToClear(InventoryConstants.Head, OneHand, _ => true));
    }

    [Theory]
    [InlineData(TwoHandRight, true)]
    [InlineData(TwoHandLeft, true)]
    [InlineData(OneHand, false)]
    [InlineData(RightOnly, false)]
    public void IsTwoHandedSlotType_IdentifiesBothTwoHandCodes(int slotType, bool expected) =>
        Assert.Equal(expected, Inventory.IsTwoHandedSlotType(slotType));

    [Fact]
    public void ApplySlotUpdate_KeepsOurDurabilityWhenTheServerOmitsIt()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, durability: 7_000);

        inv.ApplySlotUpdate(Inventory.GridStart, Item(1234, count: 2, durability: 0));

        Assert.Equal(7_000, inv[Inventory.GridStart].Durability);
        Assert.Equal(2, inv[Inventory.GridStart].Count);
    }

    [Fact]
    public void ApplySlotUpdate_TakesTheServerDurabilityWhenItSendsOne()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, durability: 7_000);

        inv.ApplySlotUpdate(Inventory.GridStart, Item(1234, durability: 42));

        Assert.Equal(42, inv[Inventory.GridStart].Durability);
    }

    [Fact]
    public void ApplySlotUpdate_DoesNotKeepDurabilityAcrossADifferentItem()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, durability: 7_000);

        inv.ApplySlotUpdate(Inventory.GridStart, Item(5678, durability: 0));

        Assert.Equal(0, inv[Inventory.GridStart].Durability);
        Assert.Equal(5678, inv[Inventory.GridStart].ItemId);
    }

    [Theory]
    [InlineData(0, 5)]      // no item id
    [InlineData(1234, 0)]   // no count
    public void ApplySlotUpdate_TreatsAMissingIdOrCountAsEmpty(int itemId, int count)
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234);

        inv.ApplySlotUpdate(Inventory.GridStart, Item(itemId, count));

        Assert.True(inv[Inventory.GridStart].IsEmpty);
    }

    [Fact]
    public void ApplySlotUpdate_GrowsTheInventoryToFitTheSlot()
    {
        var inv = new Inventory();
        inv.ApplySlotUpdate(40, Item(1234));

        Assert.Equal(41, inv.Length);
        Assert.Equal(1234, inv[40].ItemId);
    }

    [Fact]
    public void ApplySlotUpdate_IgnoresANegativeSlot()
    {
        var inv = new Inventory();
        inv.ApplySlotUpdate(-1, Item(1234));
        Assert.Equal(0, inv.Length);
    }

    [Fact]
    public void ApplyGridRefresh_ReplacesTheBag_AndLeavesWornGearAlone()
    {
        var inv = Empty();
        inv[InventoryConstants.Head] = Item(4242);

        var bag = new ItemSlot[Inventory.GridCount];
        bag[0] = Item(11);
        bag[1] = Item(22);
        inv.ApplyGridRefresh(bag);

        Assert.Equal(11, inv[Inventory.GridStart].ItemId);
        Assert.Equal(22, inv[Inventory.GridStart + 1].ItemId);
        Assert.Equal(4242, inv[InventoryConstants.Head].ItemId);
    }

    [Fact]
    public void ApplyGridRefresh_ToleratesAShortPayload()
    {
        var inv = Empty();
        inv.ApplyGridRefresh(new[] { Item(11) });    // server sent one slot, not 28

        Assert.Equal(11, inv[Inventory.GridStart].ItemId);
        Assert.True(inv[Inventory.GridStart + 1].IsEmpty);
    }

    [Fact]
    public void Stack_AddsToTheCount()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, count: 35);

        inv.Stack(Inventory.GridStart, 15);

        Assert.Equal(50, inv[Inventory.GridStart].Count);
    }

    [Fact]
    public void Stack_CapsAtStackMax_RatherThanOverflowingTheShort()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, count: Inventory.StackMax - 5);

        inv.Stack(Inventory.GridStart, 500);

        Assert.Equal(Inventory.StackMax, inv[Inventory.GridStart].Count);
    }

    [Fact]
    public void Consume_DecrementsTheStack()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, count: 5);

        inv.Consume(Inventory.GridStart, 2);

        Assert.Equal(3, inv[Inventory.GridStart].Count);
        Assert.Equal(1234, inv[Inventory.GridStart].ItemId);
    }

    [Fact]
    public void Consume_EmptiesTheSlotWhenTheStackRunsOut()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, count: 1);

        inv.Consume(Inventory.GridStart, 1);

        Assert.True(inv[Inventory.GridStart].IsEmpty);
    }

    [Fact]
    public void SetDurability_OverwritesJustTheDurability()
    {
        var inv = Empty();
        inv[Inventory.GridStart] = Item(1234, count: 3, durability: 12);

        inv.SetDurability(Inventory.GridStart, 7_000);

        Assert.Equal(7_000, inv[Inventory.GridStart].Durability);
        Assert.Equal(3, inv[Inventory.GridStart].Count);
        Assert.Equal(1234, inv[Inventory.GridStart].ItemId);
    }

    [Fact]
    public void Swap_ExchangesTwoSlots()
    {
        var inv = Empty();
        inv[InventoryConstants.RightHand] = Item(11);
        inv[Inventory.GridStart] = Item(22);

        inv.Swap(InventoryConstants.RightHand, Inventory.GridStart);

        Assert.Equal(22, inv[InventoryConstants.RightHand].ItemId);
        Assert.Equal(11, inv[Inventory.GridStart].ItemId);
    }
}
