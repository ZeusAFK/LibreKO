using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class PetBag
{
    public const byte ItemSlotCode = 26;
    public const byte AutomaticLootingKind = 170;
    private const int SlotCodeModulo = 100;

    public static bool Holds(ItemData item) => item.Slot % SlotCodeModulo == ItemSlotCode;

    public static bool Fits(ItemSlot[] bag, int position, ItemData item, Func<int, ItemData?> lookup) =>
        Holds(item) && !bag.Where((slot, index) => index != position && !slot.IsEmpty)
                           .Any(slot => lookup(slot.ItemId)?.Kind == item.Kind);

    public static bool Loots(ItemSlot[] bag, Func<int, ItemData?> lookup) =>
        bag.Any(slot => !slot.IsEmpty && lookup(slot.ItemId)?.Kind == AutomaticLootingKind);
}
