using System;
using System.Linq;

namespace LibreKO.Domain;

public static class PetBag
{
    public const int ItemSlotCode = 26;
    public const int AutomaticLootingKind = 170;
    private const int SlotCodeModulo = 100;

    public static bool Holds(ItemData.Item item) => item.Slot % SlotCodeModulo == ItemSlotCode;

    public static bool Fits(ItemSlot[] bag, int position, ItemData.Item item, Func<int, ItemData.Item?> lookup) =>
        Holds(item) && !bag.Where((slot, index) => index != position && !slot.IsEmpty)
                           .Any(slot => lookup(slot.ItemId)?.Kind == item.Kind);

    public static bool Loots(ItemSlot[] bag, Func<int, ItemData.Item?> lookup) =>
        bag.Any(slot => !slot.IsEmpty && lookup(slot.ItemId)?.Kind == AutomaticLootingKind);
}
