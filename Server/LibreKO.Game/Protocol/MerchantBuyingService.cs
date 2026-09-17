using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using static LibreKO.Game.Protocol.MerchantPacketConstants;

namespace LibreKO.Game.Protocol;

public interface IMerchantBuyingService
{
    Task OpenAsync(UserSession session);
    Task InsertAsync(UserSession session, Packet packet);
    Task ListAsync(UserSession session, Packet packet);
    Task BuyAsync(UserSession session, Packet packet);
    Task CloseAsync(UserSession session, bool broadcast);
}

public class MerchantBuyingService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    ILogger<MerchantBuyingService> logger) : IMerchantBuyingService
{
    public async Task OpenAsync(UserSession session)
    {
        var result =
            session.Hp <= 0 ? BuyingMerchantResult.WhileDead
            : session.Trade.IsTrading ? BuyingMerchantResult.WhileMerchanting
            : session.Trade.IsMerchanting || session.Trade.IsMerchantPreparing ? BuyingMerchantResult.WhileMerchanting
            : !IsBuyingMerchantZone(session) ? BuyingMerchantResult.NotAllowedHere
            : session.Level < MerchantPacketConstants.MinimumBuyingMerchantLevel ? BuyingMerchantResult.UnderLevelled
            : BuyingMerchantResult.Accepted;

        if (result == BuyingMerchantResult.Accepted)
        {
            session.Trade.IsBuyingMerchantPreparing = true;
            ClearWanted(session);
        }
        else
        {
            logger.LogDebug("Buying merchant open refused for {Name}: {Result}", session.Name, result);
        }

        await session.Client.SendPacket(MerchantPacketWriter.BuyOpenResult(result));
    }

    public async Task InsertAsync(UserSession session, Packet packet)
    {
        var wantedCount = packet.ReadByte();

        if (session.Hp <= 0
            || session.Trade.IsTrading
            || session.Trade.IsMerchanting
            || !session.Trade.IsBuyingMerchantPreparing
            || wantedCount == 0
            || wantedCount > MerchantPacketConstants.StallSlots)
        {
            await RefuseInsertAsync(session, BuyingMerchantResult.WrongStallSetup);
            return;
        }

        var wanted = new MerchantItem[MerchantPacketConstants.StallSlots];
        long totalCost = 0;

        for (var i = 0; i < wantedCount; i++)
        {
            var itemId = packet.ReadInt();
            var count = packet.ReadUShort();
            var price = packet.ReadInt();

            var itemData = gameDataService.GetItem(itemId);
            if (itemData == null || count == 0 || !IsSanePrice(price))
            {
                await RefuseInsertAsync(session, BuyingMerchantResult.WrongItemSetup);
                return;
            }

            var stack = itemData.Countable != 0 ? count : (ushort)1;
            totalCost += (long)price * stack;

            wanted[i] = new MerchantItem
            {
                ItemId = itemId,
                Count = stack,
                Price = price,
                Durability = itemData.Duration,
            };
        }

        if (totalCost > session.Money)
        {
            await RefuseInsertAsync(session, BuyingMerchantResult.SellerFundsTooLow);
            return;
        }

        for (var i = 0; i < wanted.Length; i++)
            session.Trade.BuyMerchantItems[i] = wanted[i] ?? new MerchantItem();

        session.Trade.MerchantState = MerchantMode.Buying;
        session.Trade.IsBuyingMerchantPreparing = true;
        session.Trade.MerchantTargetUserId = -1;

        logger.LogDebug("{Name} opened a buying stall wanting {Count} item kinds for up to {Cost} gold",
            session.Name, wantedCount, totalCost);

        await session.Client.SendPacket(
            MerchantPacketWriter.BuyInsertResult(BuyingMerchantResult.Accepted));

        await sessionManager.Regions.SendToRegion(
            session, MerchantPacketWriter.BuyingStallInserted(session.CharacterId, WantedItemIds(session)));
    }

    public async Task ListAsync(UserSession session, Packet packet)
    {
        var merchantId = packet.ReadInt();
        var merchant = sessionManager.GetByCharacterId(merchantId);

        if (merchant == null
            || merchant.CharacterId == session.CharacterId
            || !merchant.Trade.IsBuyingMerchant
            || session.Trade.IsMerchanting
            || session.Trade.IsTrading
            || !ExchangePacketConstants.IsWithinTradeRange(session, merchant))
        {
            session.Trade.MerchantTargetUserId = -1;
            return;
        }

        session.Trade.MerchantTargetUserId = merchant.CharacterId;

        var wanted = new List<MerchantPacketWriter.StallItem?>(MerchantPacketConstants.StallSlots);
        foreach (var item in merchant.Trade.BuyMerchantItems)
        {
            wanted.Add(item != null && !item.IsEmpty
                ? new MerchantPacketWriter.StallItem(item.ItemId, item.Count, item.Durability, item.Price)
                : null);
        }

        await session.Client.SendPacket(MerchantPacketWriter.WantedList(merchant.CharacterId, wanted));
    }

    public async Task BuyAsync(UserSession session, Packet packet)
    {
        var sellerSlot = packet.ReadByte();
        var wantedSlot = packet.ReadByte();
        var stackSize = packet.ReadUShort();

        var merchant = sessionManager.GetByCharacterId(session.Trade.MerchantTargetUserId);
        var refusal = Refusal(session, merchant, sellerSlot, wantedSlot, stackSize);
        if (refusal != BuyingMerchantResult.Accepted)
        {
            logger.LogDebug("Sale to buying merchant refused for {Name}: {Result}", session.Name, refusal);
            await session.Client.SendPacket(MerchantPacketWriter.BuyPurchaseResult(refusal));
            return;
        }

        var wantedItem = merchant!.Trade.BuyMerchantItems[wantedSlot];
        var sellerItem = session.Inventory[InventoryConstants.SlotMax + sellerSlot];
        var price = (int)((long)wantedItem.Price * stackSize);

        var merchantSlot = merchant.FindSlotForItem(wantedItem.ItemId, gameDataService, stackSize);
        if (merchantSlot < 0)
        {
            await session.Client.SendPacket(
                MerchantPacketWriter.BuyPurchaseResult(BuyingMerchantResult.InventoryFull));
            return;
        }

        var merchantItem = merchant.Inventory[merchantSlot];
        var merchantItemIsNew = merchantItem.IsEmpty;

        merchant.Money -= price;
        session.Money += price;

        merchantItem.ItemId = wantedItem.ItemId;
        merchantItem.Durability = sellerItem.Durability;
        merchantItem.Count += stackSize;

        sellerItem.Count -= stackSize;
        wantedItem.Count -= stackSize;

        if (sellerItem.Count == 0)
            sellerItem.Clear();
        if (wantedItem.Count == 0)
            merchant.Trade.BuyMerchantItems[wantedSlot] = new MerchantItem();

        logger.LogInformation("{SellerName} sold item {ItemId} x{Count} to buying merchant {MerchantName} for {Price} gold",
            session.Name, merchantItem.ItemId, stackSize, merchant.Name, price);

        session.RecalculateStatsWithBuffs(gameDataService);
        merchant.RecalculateStatsWithBuffs(gameDataService);

        await userNotificationService.SendStackChangeAsync(
            session, sellerSlot, sellerItem.ItemId, sellerItem.Count, sellerItem.Durability);
        await userNotificationService.SendStackChangeAsync(
            merchant, (byte)(merchantSlot - InventoryConstants.SlotMax),
            merchantItem.ItemId, merchantItem.Count, merchantItem.Durability, merchantItemIsNew);

        await session.Client.SendPacket(MerchantPacketWriter.WantedItemSold(
            wantedSlot, merchant.Trade.BuyMerchantItems[wantedSlot].Count, sellerSlot, sellerItem.Count));
        await session.Client.SendPacket(
            MerchantPacketWriter.BuyPurchaseResult(BuyingMerchantResult.Accepted));

        await merchant.Client.SendPacket(MerchantPacketWriter.WantedItemBought(
            wantedSlot, merchant.Trade.BuyMerchantItems[wantedSlot].Count, session.Name));

        await userNotificationService.SendGoldGainAsync(session, price);
        await userNotificationService.SendGoldLossAsync(merchant, price);
        await userNotificationService.SendWeightChangeAsync(session);
        await userNotificationService.SendWeightChangeAsync(merchant);

        if (merchant.Trade.BuyMerchantItems.All(entry => entry == null || entry.IsEmpty))
            await CloseAsync(merchant, broadcast: true);
        else
            await sessionManager.Regions.SendToRegion(
                merchant, MerchantPacketWriter.BuyingStallInserted(merchant.CharacterId, WantedItemIds(merchant)));
    }

    public async Task CloseAsync(UserSession session, bool broadcast)
    {
        if (!session.Trade.IsBuyingMerchant && !session.Trade.IsBuyingMerchantPreparing)
            return;

        ClearWanted(session);
        session.Trade.IsBuyingMerchantPreparing = false;
        if (session.Trade.IsBuyingMerchant)
            session.Trade.MerchantState = MerchantMode.None;
        session.Trade.MerchantTargetUserId = -1;

        if (!broadcast)
            return;

        await sessionManager.Regions.SendToRegion(
            session, MerchantPacketWriter.BuyingStallClosed(session.CharacterId), excludeSender: false);
    }

    private BuyingMerchantResult Refusal(
        UserSession session, UserSession? merchant, byte sellerSlot, byte wantedSlot, ushort stackSize)
    {
        if (merchant == null || !merchant.Trade.IsBuyingMerchant || merchant.CharacterId == session.CharacterId)
            return BuyingMerchantResult.WrongStallSetup;

        if (session.Hp <= 0)
            return BuyingMerchantResult.WhileDead;

        if (session.Trade.IsTrading || session.Trade.IsMerchanting || session.Trade.IsMerchantPreparing)
            return BuyingMerchantResult.WhileMerchanting;

        if (!ExchangePacketConstants.IsWithinTradeRange(session, merchant))
            return BuyingMerchantResult.NotAllowedHere;

        if (sellerSlot >= InventoryConstants.HaveMax
            || wantedSlot >= MerchantPacketConstants.StallSlots
            || stackSize == 0)
            return BuyingMerchantResult.WrongPurchaseCount;

        var wantedItem = merchant.Trade.BuyMerchantItems[wantedSlot];
        if (wantedItem == null || wantedItem.IsEmpty || wantedItem.Count < stackSize)
            return BuyingMerchantResult.NoSuchItemWanted;

        var sellerItem = session.Inventory[InventoryConstants.SlotMax + sellerSlot];
        if (sellerItem.IsEmpty || sellerItem.ItemId != wantedItem.ItemId || sellerItem.Count < stackSize)
            return BuyingMerchantResult.NoSuchItemWanted;

        if (!sellerItem.IsTradable)
            return BuyingMerchantResult.ItemNotSellable;

        if (IsNoTradeItem(sellerItem.ItemId))
            return BuyingMerchantResult.ItemNotSellable;

        var itemData = gameDataService.GetItem(wantedItem.ItemId);
        if (itemData == null)
            return BuyingMerchantResult.WrongItemSetup;

        if (itemData.Countable == 0 && stackSize != 1)
            return BuyingMerchantResult.WrongPurchaseCount;

        if (sellerItem.Durability < wantedItem.Durability)
            return BuyingMerchantResult.NeedsRepair;

        var price = (long)wantedItem.Price * stackSize;
        if (!IsSanePrice(price) || price > merchant.Money)
            return BuyingMerchantResult.BuyerFundsTooLow;

        if (!CanReceive(session, price))
            return BuyingMerchantResult.OverMaxLimit;

        return BuyingMerchantResult.Accepted;
    }

    private async Task RefuseInsertAsync(UserSession session, BuyingMerchantResult result)
    {
        logger.LogDebug("Buying merchant insert refused for {Name}: {Result}", session.Name, result);
        await session.Client.SendPacket(MerchantPacketWriter.BuyInsertResult(result));
        await CloseAsync(session, broadcast: false);
    }

    private static void ClearWanted(UserSession session)
    {
        for (var i = 0; i < session.Trade.BuyMerchantItems.Length; i++)
            session.Trade.BuyMerchantItems[i] = new MerchantItem();
    }

    private static List<int> WantedItemIds(UserSession session) =>
        session.Trade.BuyMerchantItems
            .Select(item => item != null && !item.IsEmpty ? item.ItemId : 0)
            .ToList();

    private static bool IsBuyingMerchantZone(UserSession session) =>
        (ZoneId)session.ZoneId is ZoneId.Moradon or ZoneId.Moradon2 or ZoneId.Moradon3
            or ZoneId.Moradon4 or ZoneId.Moradon5;
}
