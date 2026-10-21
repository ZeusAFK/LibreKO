using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class ItemTransfer
{
    public static bool Fits(ItemSlot source, ItemSlot destination, ItemData data, int count) =>
        destination.IsEmpty
        || data.Countable != 0
            && destination.ItemId == source.ItemId
            && destination.Flag == source.Flag
            && destination.ExpiresAt == source.ExpiresAt
            && destination.UniqueId == 0
            && source.UniqueId == 0
            && destination.Count + count <= InventoryConstants.MaxStackCount;

    public static void Move(ItemSlot source, ItemSlot destination, int count)
    {
        if (destination.IsEmpty)
        {
            destination.ItemId = source.ItemId;
            destination.Durability = source.Durability;
            destination.Flag = source.Flag;
            destination.ExpiresAt = source.ExpiresAt;
            destination.UniqueId = source.UniqueId;
        }

        destination.Count += (ushort)count;
        source.Count -= (ushort)count;
        if (source.Count == 0)
            source.Clear();
    }
}
