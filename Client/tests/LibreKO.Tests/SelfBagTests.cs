using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class SelfBagTests
{
    private const int Armour = 190_250_001;
    private const int Potion = 389_010_000;
    private const short ArmourDurability = 7_000;
    private const short PotionCount = 5;
    private const int Kaul = 610_001_000;
    private const int KaulFamiliarIndex = 77;

    private static SelfSession Entered(params (int Abs, ItemSlot Slot)[] items)
    {
        var bag = new ItemSlot[InventoryConstants.InventoryTotal];
        foreach (var (abs, slot) in items) bag[abs] = slot;
        var self = new SelfSession();
        self.ApplyMyInfo(new MyInfo { Name = "Zeus" }, bag);
        return self;
    }

    private static ItemSlot Item(int itemId, short count = 1, short durability = 1) =>
        new() { ItemId = itemId, Count = count, Durability = durability };

    private static int GearAt(SelfSession self, int abs) =>
        self.LastEnter.Gear[Array.IndexOf(InventoryConstants.VisualSlots, abs)];

    [Fact]
    public void TheCharacterRecordSeedsTheBag()
    {
        var self = Entered((Inventory.GridStart, Item(Potion, PotionCount)));

        Assert.Equal(InventoryConstants.InventoryTotal, self.Inventory.Length);
        Assert.Equal(Potion, self.Inventory[Inventory.GridStart].ItemId);
    }

    [Fact]
    public void WearingAPieceRefreshesTheGear()
    {
        var self = Entered();

        self.SetInventorySlot(InventoryConstants.Breast, Item(Armour, durability: ArmourDurability));

        Assert.Equal(Armour, GearAt(self, InventoryConstants.Breast));
    }

    [Fact]
    public void AnEmptyCountClearsTheSlot()
    {
        var self = Entered((Inventory.GridStart, Item(Potion, PotionCount)));

        self.SetInventorySlot(Inventory.GridStart, Item(Potion, count: 0));

        Assert.True(self.Inventory[Inventory.GridStart].IsEmpty);
    }

    [Fact]
    public void AServerRecordWithoutDurabilityKeepsTheKnownOne()
    {
        var self = Entered((Inventory.GridStart, Item(Armour, durability: ArmourDurability)));

        self.ApplyInventorySlot(Inventory.GridStart, Item(Armour, durability: 0));

        Assert.Equal(ArmourDurability, self.Inventory[Inventory.GridStart].Durability);
    }

    [Fact]
    public void AWornOutPieceIsWrittenAsItIs()
    {
        var self = Entered((Inventory.GridStart, Item(Armour, durability: ArmourDurability)));

        self.SetInventorySlot(Inventory.GridStart, Item(Armour, durability: 0));

        Assert.Equal(0, self.Inventory[Inventory.GridStart].Durability);
    }

    [Fact]
    public void KaulIsEquippedInThePetSlot()
    {
        var kaul = new ItemData.Item { Slot = ItemData.SlotType.Pet };
        var self = Entered();

        Assert.Equal(InventoryConstants.Pet, self.Inventory.ResolveEquipDest(kaul.Slot, ItemData.EquipSlotFor(kaul)));
    }

    [Fact]
    public void AnEquippedKaulKeepsItsFamiliarAndShowsAsThePet()
    {
        var kaul = new ItemSlot { ItemId = Kaul, Count = 1, Durability = 1, UniqueId = KaulFamiliarIndex };
        var self = Entered((Inventory.GridStart, kaul));

        self.ApplyItemMove(ItemMoveDirection.InventoryToSlot, Inventory.GridStart, InventoryConstants.Pet, ItemMove.WholeStack);

        Assert.True(self.Inventory[Inventory.GridStart].IsEmpty);
        Assert.Equal(KaulFamiliarIndex, self.Inventory[InventoryConstants.Pet].UniqueId);
        Assert.Equal(Kaul, GearAt(self, InventoryConstants.Pet));
    }

    [Fact]
    public void AConfirmedMoveChangesTheBagAndTheGear()
    {
        var self = Entered((Inventory.GridStart, Item(Armour, durability: ArmourDurability)));

        self.ApplyItemMove(ItemMoveDirection.InventoryToSlot, Inventory.GridStart, InventoryConstants.Breast, ItemMove.WholeStack);

        Assert.True(self.Inventory[Inventory.GridStart].IsEmpty);
        Assert.Equal(Armour, self.Inventory[InventoryConstants.Breast].ItemId);
        Assert.Equal(Armour, GearAt(self, InventoryConstants.Breast));
    }
}
