using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class PetBag
{
    public const byte ItemSlotCode = 26;
    public const byte AutomaticLootingKind = 170;
    public const byte AttackPacketKind = 172;
    public const byte DefencePacketKind = 173;
    public const byte OwnerHpScrollKind = 177;
    public const byte OwnerAcScrollKind = 178;
    public const byte OwnerAttackScrollKind = 179;
    public const int PacketBonusPercent = 20;
    private const int SlotCodeModulo = 100;
    private const int PercentScale = 100;

    public static bool Holds(ItemData item) => item.Slot % SlotCodeModulo == ItemSlotCode;

    public static bool Fits(ItemSlot[] bag, int position, ItemData item, Func<int, ItemData?> lookup) =>
        Holds(item) && !bag.Where((slot, index) => index != position && !slot.IsEmpty)
                           .Any(slot => lookup(slot.ItemId)?.Kind == item.Kind);

    public static bool Loots(ItemSlot[] bag, Func<int, ItemData?> lookup) => Has(bag, AutomaticLootingKind, lookup);

    public static short Boost(short value, ItemSlot[] bag, byte packetKind, Func<int, ItemData?> lookup) =>
        Has(bag, packetKind, lookup) ? (short)Math.Min(short.MaxValue, value * (PercentScale + PacketBonusPercent) / PercentScale) : value;

    public static IEnumerable<ItemData> OwnerScrolls(ItemSlot[] bag, Func<int, ItemData?> lookup) =>
        bag.Where(slot => !slot.IsEmpty)
            .Select(slot => lookup(slot.ItemId))
            .OfType<ItemData>()
            .Where(item => item.Kind is OwnerHpScrollKind or OwnerAcScrollKind or OwnerAttackScrollKind);

    private static bool Has(ItemSlot[] bag, byte kind, Func<int, ItemData?> lookup) =>
        bag.Any(slot => !slot.IsEmpty && lookup(slot.ItemId)?.Kind == kind);
}
