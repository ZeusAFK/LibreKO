using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

using static LibreKO.Game.Protocol.MerchantPacketConstants;

namespace LibreKO.Game.Protocol;

public interface IMerchantListingService
{
    Task AddItemAsync(UserSession session, Packet packet);
    Task CancelItemAsync(UserSession session, Packet packet);
    Task ListItemsAsync(UserSession session, Packet packet);
    Task BuyItemAsync(UserSession session, Packet packet);
}

public class MerchantListingService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IMerchantLifecycleService merchantLifecycleService,
    IMarketPriceService marketPriceService,
    IDailyRankService dailyRankService,
    ISlaveMerchantService slaveMerchants,
    ILogger<MerchantListingService> logger) : IMerchantListingService
{
    public async Task AddItemAsync(UserSession session, Packet packet)
    {
        var itemId = packet.ReadInt();
        var count = packet.ReadUShort();
        var price = packet.ReadInt();
        var srcPos = packet.ReadByte();
        var dstPos = packet.ReadByte();

        var itemData = gameDataService.GetItem(itemId);
        var refusal =
            !StallBeingSetUp(session) ? "no stall is being set up"
            : itemData == null ? "no such item"
            : srcPos >= InventoryConstants.HaveMax ? "source slot out of range"
            : dstPos >= session.Trade.MerchantItems.Length ? "stall slot out of range"
            : IsNoTradeItem(itemId) ? "item cannot be traded"
            : !IsSanePrice(price) ? "price out of range"
            : count == 0 ? "count is zero"
            : itemData.Countable == 0 && count != 1 ? "not stackable but count is not 1"
            : session.Trade.MerchantItems[dstPos] is { IsEmpty: false } ? "stall slot already taken"
            : session.Trade.MerchantItems.Any(item =>
                item is { IsEmpty: false } && item.OriginalSlot == InventoryConstants.SlotMax + srcPos)
                ? "that bag slot is already listed"
            : null;

        var absPos = InventoryConstants.SlotMax + srcPos;
        if (refusal == null)
        {
            var held = session.Inventory[absPos];
            refusal =
                held.ItemId != itemId ? $"slot {absPos} holds {held.ItemId}, not {itemId}"
                : held.Count < count ? $"slot {absPos} holds {held.Count}, fewer than {count}"
                : !held.IsTradable ? $"item is {held.State}"
                : gameDataService.GetItem(itemId)?.IsUntradeable == true ? "item cannot be traded"
                : null;
        }

        if (refusal != null)
        {
            logger.LogDebug(
                "Merchant add refused for {Name}: {Reason} (item {ItemId} x{Count} at {SrcPos} -> stall {DstPos}, price {Price})",
                session.Name, refusal, itemId, count, srcPos, dstPos, price);

            await session.Client.SendPacket(
                MerchantPacketWriter.Refused(MerchantSubOpcode.ItemAdd, MerchantResult.CannotTrade));
            return;
        }

        var slot = session.Inventory[absPos];

        session.Trade.MerchantItems[dstPos] = new MerchantItem
        {
            ItemId = itemId,
            Durability = slot.Durability,
            Count = count,
            Price = price,
            OriginalSlot = (byte)absPos,
            Flag = slot.Flag,
            ExpiresAt = slot.ExpiresAt,
            UniqueId = slot.UniqueId,
        };

        await session.Client.SendPacket(MerchantPacketWriter.ItemAdded(
            MerchantSubOpcode.ItemAdd, itemId, count,
            session.Trade.MerchantItems[dstPos].Durability, price, srcPos, dstPos));
    }

    public async Task CancelItemAsync(UserSession session, Packet packet)
    {
        var slotIndex = packet.ReadByte();

        if (slotIndex >= session.Trade.MerchantItems.Length || !StallBeingSetUp(session))
        {
            await session.Client.SendPacket(
                MerchantPacketWriter.Result(MerchantSubOpcode.ItemCancel, MerchantPacketWriter.Failed));
            return;
        }

        var merchantItem = session.Trade.MerchantItems[slotIndex];
        if (merchantItem == null || merchantItem.IsEmpty)
        {
            await session.Client.SendPacket(
                MerchantPacketWriter.Result(MerchantSubOpcode.ItemCancel, MerchantPacketWriter.Failed));
            return;
        }

        session.Trade.MerchantItems[slotIndex] = new MerchantItem();

        await session.Client.SendPacket(MerchantPacketWriter.ItemCancelled(
            MerchantSubOpcode.ItemCancel, slotIndex));
    }

    // A stall's listings change only while it is being set up.
    private static bool StallBeingSetUp(UserSession session) =>
        session.Trade.IsSellingMerchantPreparing && !session.Trade.IsMerchanting;

    public async Task ListItemsAsync(UserSession session, Packet packet)
    {
        var targetId = packet.ReadInt();
        var merchant = sessionManager.GetByCharacterId(targetId);
        if (merchant == null || !merchant.Trade.IsMerchanting)
        {
            session.Trade.MerchantTargetUserId = -1;
            return;
        }

        session.Trade.MerchantTargetUserId = merchant.CharacterId;
        logger.LogDebug("{Name} browsing merchant shop of {MerchantName}", session.Name, merchant.Name);

        var stall = new List<MerchantPacketWriter.StallItem?>(session.Trade.MerchantItems.Length);
        for (var i = 0; i < session.Trade.MerchantItems.Length; i++)
        {
            var item = merchant.Trade.MerchantItems[i];
            stall.Add(item != null && !item.IsEmpty
                ? new MerchantPacketWriter.StallItem(item.ItemId, item.Count, item.Durability, item.Price)
                : null);
        }

        await session.Client.SendPacket(MerchantPacketWriter.StallContents(
            MerchantSubOpcode.ItemList, targetId, stall));
    }

    private Task RefuseBuyAsync(UserSession session) =>
        session.Client.SendPacket(
            MerchantPacketWriter.Refused(MerchantSubOpcode.ItemBuy, MerchantResult.CannotTrade));

    public async Task BuyItemAsync(UserSession session, Packet packet)
    {
        var itemId = packet.ReadInt();
        var count = packet.ReadUShort();
        var merchantSlot = packet.ReadByte();
        packet.ReadByte();

        var merchant = sessionManager.GetByCharacterId(session.Trade.MerchantTargetUserId);
        if (merchant == null || !merchant.Trade.IsMerchanting || IsOwnStall(merchant, session))
        {
            session.Trade.MerchantTargetUserId = -1;
            await RefuseBuyAsync(session);
            return;
        }

        var purchase = new MerchantPurchase(session, merchant, itemId, count, merchantSlot);
        var sold = merchant is SlaveMerchantBotSession slave
            ? await slaveMerchants.SellAsync(slave, purchase, () => AnnounceAsync(purchase))
            : await SellAsync(purchase);
        if (sold)
            return;

        logger.LogDebug("Merchant buy refused for {Name}: {Reason} (item {ItemId} x{Count} from stall {Slot} of {Merchant})",
            session.Name, purchase.Refusal ?? "the sale could not be saved", itemId, count, merchantSlot, merchant.Name);
        await RefuseBuyAsync(session);
    }

    private async Task<bool> SellAsync(MerchantPurchase purchase)
    {
        var applied = false;
        UserSession.WithBoth(purchase.Buyer, purchase.Merchant, (_, _) => applied = purchase.TryApply(gameDataService));
        if (!applied)
            return false;

        await AnnounceAsync(purchase);
        if (purchase.StallIsEmpty)
            await merchantLifecycleService.CloseAsync(purchase.Merchant, MerchantInOut.StallClosed);
        return true;
    }

    private async Task AnnounceAsync(MerchantPurchase purchase)
    {
        var buyer = purchase.Buyer;
        var merchant = purchase.Merchant;
        logger.LogInformation("{BuyerName} bought item {ItemId} x{Count} from {SellerName} for {Cost} gold",
            buyer.Name, purchase.ItemId, purchase.Count, merchant.Name, purchase.Cost);

        var destination = buyer.Inventory[purchase.Destination];
        var sellerSlot = merchant.Inventory[purchase.SellerSlot];
        buyer.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendStackChangeAsync(buyer, (byte)purchase.Destination,
            destination.ItemId, destination.Count, destination.Durability, purchase.DestinationIsNew);
        await userNotificationService.SendStackChangeAsync(merchant, purchase.SellerSlot,
            sellerSlot.ItemId, sellerSlot.Count, sellerSlot.Durability);
        await buyer.Client.SendPacket(MerchantPacketWriter.ItemBought(
            MerchantSubOpcode.ItemBuy, purchase.ItemId, purchase.RemainingCount, purchase.MerchantSlot,
            (byte)(purchase.Destination - InventoryConstants.SlotMax)));
        await userNotificationService.SendGoldLossAsync(buyer, (int)purchase.Cost);
        await userNotificationService.SendGoldGainAsync(merchant, (int)purchase.Cost);
        await userNotificationService.SendWeightChangeAsync(buyer);
        merchant.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendWeightChangeAsync(merchant);

        await merchant.Client.SendPacket(MerchantPacketWriter.ItemSold(
            MerchantSubOpcode.ItemPurchased, purchase.ItemId, buyer.Name));
        await marketPriceService.RecordAsync(buyer, merchant, purchase.ItemId, purchase.UnitPrice, purchase.Count);
        await dailyRankService.CountMerchantSaleAsync(merchant, purchase.Cost);

        if (purchase.RemainingCount == 0 && !purchase.StallIsEmpty
            && purchase.MerchantSlot < MerchantLifecycleService.ShowcaseSlots(merchant))
            await sessionManager.Regions.SendToRegion(merchant, ShowcasePacket(merchant, purchase.MerchantSlot), excludeSender: false);
    }

    private static Packet ShowcasePacket(UserSession merchant, byte slot)
    {
        var flags = MerchantLifecycleService.StallFlags(merchant);
        return merchant.Trade.PremiumMerchant
            ? MerchantPacketWriter.ShowcaseRefreshed(merchant.CharacterId, flags, MerchantLifecycleService.DisplayItemIds(merchant))
            : MerchantPacketWriter.ShowcaseSlotEmptied(merchant.CharacterId, flags, slot);
    }
}
