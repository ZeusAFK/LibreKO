using LibreKO.Common.Domain.Services;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IMerchantInventoryRestoreService
{
    bool TryRestoreMerchantItem(UserSession session, MerchantItem merchantItem);
}

public class MerchantInventoryRestoreService(IGameDataService gameDataService) : IMerchantInventoryRestoreService
{
    public bool TryRestoreMerchantItem(UserSession session, MerchantItem merchantItem)
    {
        var restoreSlot = ResolveRestoreSlot(session, merchantItem);
        if (restoreSlot < 0)
            return false;

        var inventorySlot = session.Inventory[restoreSlot];
        var isNewItem = inventorySlot.IsEmpty;
        inventorySlot.ItemId = merchantItem.ItemId;
        inventorySlot.Count += merchantItem.Count;
        if (isNewItem || inventorySlot.Durability == 0)
            inventorySlot.Durability = merchantItem.Durability;

        return true;
    }

    private int ResolveRestoreSlot(UserSession session, MerchantItem merchantItem)
    {
        if (merchantItem.OriginalSlot < session.Inventory.Length)
        {
            var originalSlot = session.Inventory[merchantItem.OriginalSlot];
            if (originalSlot.IsEmpty)
                return merchantItem.OriginalSlot;

            var itemData = gameDataService.GetItem(merchantItem.ItemId);
            if (itemData?.Countable != 0
                && originalSlot.ItemId == merchantItem.ItemId
                && originalSlot.Count + merchantItem.Count <= 9999)
            {
                return merchantItem.OriginalSlot;
            }
        }

        return session.FindSlotForItem(merchantItem.ItemId, gameDataService, merchantItem.Count);
    }
}
