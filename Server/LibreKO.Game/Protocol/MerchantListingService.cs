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
            itemData == null ? "no such item"
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
            OriginalSlot = (byte)absPos
        };

        await session.Client.SendPacket(MerchantPacketWriter.ItemAdded(
            MerchantSubOpcode.ItemAdd, itemId, count,
            session.Trade.MerchantItems[dstPos].Durability, price, srcPos, dstPos));
    }

    public async Task CancelItemAsync(UserSession session, Packet packet)
    {
        var slotIndex = packet.ReadByte();

        if (slotIndex >= session.Trade.MerchantItems.Length)
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
        var buyerSlot = packet.ReadByte();

        if (merchantSlot >= session.Trade.MerchantItems.Length || count == 0)
        {
            await RefuseBuyAsync(session);
            return;
        }

        var merchant = sessionManager.GetByCharacterId(session.Trade.MerchantTargetUserId);
        if (merchant == null || !merchant.Trade.IsMerchanting || merchant.CharacterId == session.CharacterId)
        {
            session.Trade.MerchantTargetUserId = -1;
            await RefuseBuyAsync(session);
            return;
        }

        var merchantItem = merchant.Trade.MerchantItems[merchantSlot];
        var itemData = gameDataService.GetItem(itemId);
        if (merchantItem == null
            || merchantItem.IsEmpty
            || merchantItem.ItemId != itemId
            || merchantItem.Count < count
            || itemData == null
            || (itemData.Countable == 0 && count != 1))
        {
            await RefuseBuyAsync(session);
            return;
        }

        var totalCost = (long)merchantItem.Price * count;
        if (!IsSanePrice(totalCost) || totalCost > session.Money || !CanReceive(merchant, totalCost))
        {
            await RefuseBuyAsync(session);
            return;
        }

        var destination = session.FindSlotForItem(itemId, gameDataService, count);
        if (destination < 0)
        {
            logger.LogDebug("Merchant buy refused for {Name}: no free bag slot for {ItemId}", session.Name, itemId);
            await RefuseBuyAsync(session);
            return;
        }

        buyerSlot = (byte)(destination - InventoryConstants.SlotMax);
        var destinationSlot = session.Inventory[destination];
        session.Money -= (int)totalCost;
        merchant.Money += (int)totalCost;

        if (destinationSlot.IsEmpty)
        {
            destinationSlot.ItemId = merchantItem.ItemId;
            destinationSlot.Count = count;
            destinationSlot.Durability = merchantItem.Durability;
        }
        else
        {
            destinationSlot.Count += count;
        }

        merchantItem.Count -= count;
        var remainingCount = merchantItem.Count;

        var sellerSlot = merchant.Inventory[merchantItem.OriginalSlot];
        if (sellerSlot.ItemId == merchantItem.ItemId)
        {
            sellerSlot.Count -= Math.Min(count, sellerSlot.Count);
            if (sellerSlot.Count == 0)
                sellerSlot.Clear();
        }

        if (remainingCount == 0)
            merchant.Trade.MerchantItems[merchantSlot] = new MerchantItem();

        logger.LogInformation("{BuyerName} bought item {ItemId} x{Count} from {SellerName} for {Cost} gold",
            session.Name, itemId, count, merchant.Name, totalCost);

        session.RecalculateStatsWithBuffs(gameDataService);
        var buyResult = MerchantPacketWriter.ItemBought(
            MerchantSubOpcode.ItemBuy, itemId, remainingCount, merchantSlot, buyerSlot);
        await session.Client.SendPacket(buyResult);
        await userNotificationService.SendGoldLossAsync(session, (int)totalCost);
        await userNotificationService.SendGoldGainAsync(merchant, (int)totalCost);
        await userNotificationService.SendWeightChangeAsync(session);

        var soldNotify = MerchantPacketWriter.ItemSold(
            MerchantSubOpcode.ItemPurchased, itemId, session.Name);
        await merchant.Client.SendPacket(soldNotify);

        if (merchant.Trade.MerchantItems.All(entry => entry == null || entry.IsEmpty))
            await merchantLifecycleService.CloseAsync(merchant, MerchantInOut.StallClosed);
    }
}
