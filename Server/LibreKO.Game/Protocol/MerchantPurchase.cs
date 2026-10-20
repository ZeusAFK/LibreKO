using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.World;

using static LibreKO.Game.Protocol.MerchantPacketConstants;

namespace LibreKO.Game.Protocol;

public sealed class MerchantPurchase(UserSession buyer, UserSession merchant, int itemId, ushort count, byte merchantSlot)
{
    private readonly InventoryJournal buyerJournal = new(buyer);
    private readonly InventoryJournal merchantJournal = new(merchant);

    public UserSession Buyer => buyer;
    public UserSession Merchant => merchant;
    public int ItemId => itemId;
    public ushort Count => count;
    public byte MerchantSlot => merchantSlot;
    public int UnitPrice { get; private set; }
    public long Cost { get; private set; }
    public int Destination { get; private set; } = -1;
    public bool DestinationIsNew { get; private set; }
    public byte SellerSlot { get; private set; }
    public ushort RemainingCount { get; private set; }
    public string? Refusal { get; private set; }

    public bool StallIsEmpty => merchant.Trade.MerchantItems.All(entry => entry == null || entry.IsEmpty);

    public bool TryApply(IGameDataService gameData)
    {
        Refusal = Check(gameData);
        if (Refusal != null)
            return false;

        var entry = merchantJournal.StallEntry(merchantSlot)!;
        var sellerSlot = merchantJournal.Slot(entry.OriginalSlot);
        var destination = buyerJournal.Slot(Destination);

        buyerJournal.AddMoney(-Cost);
        merchantJournal.AddMoney(Cost);

        DestinationIsNew = destination.IsEmpty;
        if (DestinationIsNew)
        {
            InventoryJournal.CopyInto(sellerSlot, destination);
            destination.Count = count;
        }
        else
        {
            destination.Count += count;
        }

        sellerSlot.Count -= count;
        if (sellerSlot.Count == 0)
            sellerSlot.Clear();

        entry.Count -= count;
        RemainingCount = entry.Count;
        SellerSlot = entry.OriginalSlot;
        if (RemainingCount == 0)
            merchantJournal.ClearStallEntry(merchantSlot);

        buyerJournal.Seal();
        merchantJournal.Seal();
        return true;
    }

    public void Undo()
    {
        buyerJournal.Restore();
        merchantJournal.Restore();
    }

    private string? Check(IGameDataService gameData)
    {
        if (merchantSlot >= merchant.Trade.MerchantItems.Length || count == 0)
            return "bad request";
        if (!merchant.Trade.IsMerchanting)
            return "the stall is closed";
        if (buyer.Trade.ViewedStallGeneration != merchant.Trade.StallGeneration)
            return "the stall closed since the buyer looked at it";
        if (IsOwnStall(merchant, buyer))
            return "that is the buyer's own stall";
        if (buyer.IsBusy)
            return "the buyer is busy";
        if (!ExchangePacketConstants.IsWithinTradeRange(buyer, merchant))
            return "the buyer is not at the stall";

        var entry = merchant.Trade.MerchantItems[merchantSlot];
        var itemData = gameData.GetItem(itemId);
        if (entry == null || entry.IsEmpty || entry.ItemId != itemId || entry.Count < count
            || entry.OriginalSlot >= merchant.Inventory.Length)
            return "the stall no longer holds that";
        if (itemData == null || (itemData.Countable == 0 && count != 1))
            return "not stackable but count is not 1";

        var sellerSlot = merchant.Inventory[entry.OriginalSlot];
        if (sellerSlot.ItemId != itemId || sellerSlot.Count < count || !sellerSlot.IsTradable
            || sellerSlot.Flag != entry.Flag || sellerSlot.ExpiresAt != entry.ExpiresAt || sellerSlot.UniqueId != entry.UniqueId)
            return "the seller no longer holds that";
        if (!ExchangeTransferService.MayChangeHands(itemData, sellerSlot))
            return "the item cannot change hands";

        UnitPrice = entry.Price;
        Cost = (long)UnitPrice * count;
        if (!IsSanePrice(Cost) || Cost > buyer.Money || !CanReceive(merchant, Cost))
            return "price out of range";

        Destination = ItemPlacement.FindForDelivery(buyer.Inventory, InventoryConstants.InventoryStart, InventoryConstants.HaveMax,
            new ExchangeItem
            {
                ItemId = itemId, Count = count, Durability = sellerSlot.Durability, Flag = sellerSlot.Flag,
                ExpiresAt = sellerSlot.ExpiresAt, UniqueId = sellerSlot.UniqueId, Countable = itemData.Countable != 0,
            });
        return Destination < 0 ? "no free bag slot" : null;
    }
}
