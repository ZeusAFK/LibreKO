using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

internal static class StarterCharacterLoadout
{
    public static byte[] CreateInitialItems(short classId)
    {
        var inventory = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var index = 0; index < inventory.Length; index++)
            inventory[index] = new ItemSlot();

        EnsureStarterWeapon(inventory, classId, level: 1, experience: 0);
        return UserSessionBinaryState.SerializeItems(inventory);
    }

    public static void EnsureStarterWeapon(ItemSlot[] inventory, short classId, byte level, long experience)
    {
        if (level != 1 || experience != 0 || !TryGetStarterWeapon(classId, out var itemId, out var durability))
            return;

        for (var index = 0; index < inventory.Length; index++)
        {
            var slot = inventory[index];
            if (slot != null && slot.ItemId == itemId && slot.Count > 0)
                return;
        }

        for (var index = InventoryConstants.InventoryStart;
             index < InventoryConstants.InventoryStart + InventoryConstants.HaveMax && index < inventory.Length;
             index++)
        {
            var slot = inventory[index];
            if (slot == null || !slot.IsEmpty)
                continue;

            slot.ItemId = itemId;
            slot.Durability = durability;
            slot.Count = 1;
            return;
        }
    }

    private static bool TryGetStarterWeapon(short classId, out int itemId, out short durability)
    {
        var karus = ClassIdHelper.GetNation(classId) == AccountNation.Karus;
        (itemId, durability) = (ClassSubtype)ClassIdHelper.GetSubtype(classId) switch
        {
            ClassSubtype.WarriorBeginner => (karus ? 120010000 : 120050000, (short)5000),
            ClassSubtype.RogueBeginner => (karus ? 110010000 : 110050000, (short)4000),
            ClassSubtype.MageBeginner => (karus ? 180010000 : 180050000, (short)5000),
            ClassSubtype.PriestBeginner => (karus ? 190010000 : 190050000, (short)10000),
            ClassSubtype.KurianBeginner => (1110110000, (short)7000),
            _ => (0, (short)0),
        };

        return itemId != 0;
    }
}
