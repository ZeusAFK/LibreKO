using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IExchangeTransferService
{
    Task AddAsync(UserSession session, Packet packet);
    Task DecideAsync(UserSession session);
}

public class ExchangeTransferService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService,
    IExchangeLifecycleService exchangeLifecycleService,
    ICharacterSealService characterSeal,
    ILogger<ExchangeTransferService> logger) : IExchangeTransferService
{
    public async Task AddAsync(UserSession session, Packet packet)
    {
        if (!session.Trade.IsTrading)
            return;

        var target = sessionManager.GetByCharacterId(session.Trade.ExchangeUser);
        if (target == null || target.Hp <= 0 || session.Hp <= 0)
        {
            await exchangeLifecycleService.CancelAsync(session);
            return;
        }

        if (!ExchangePacketConstants.IsWithinTradeRange(session, target))
        {
            await exchangeLifecycleService.CancelAsync(session);
            return;
        }

        var pos = packet.ReadByte();
        var itemId = packet.ReadInt();
        var count = packet.ReadInt();

        if (count <= 0)
        {
            await SendAddFailAsync(session);
            return;
        }

        var itemData = itemId != InventoryConstants.ItemGold ? gameDataService.GetItem(itemId) : null;

        if (itemId != InventoryConstants.ItemGold && pos < InventoryConstants.HaveMax
            && session.Inventory[InventoryConstants.SlotMax + pos] is { } offered
            && offered.ItemId == itemId && offered.State == ItemFlag.Duplicate)
        {
            await session.Client.SendPacket(ExchangePacketWriter.Sub(ExchangePacketConstants.ExchangeDuplicate));
            await SendAddFailAsync(session);
            return;
        }

        if (itemId != InventoryConstants.ItemGold && !IsTradableItem(itemData, itemId, pos))
        {
            await SendAddFailAsync(session);
            return;
        }

        // The offer is taken out of the bag into escrow, so nothing is left behind in the slot.
        var outcome = session.WithLock(s =>
        {
            if (s.Trade.ExchangeOk || !s.Trade.ExchangeAccepted || s.Trade.ExchangeUser != target.CharacterId)
                return (Success: false, Duration: (short)0, UniqueId: 0);

            if (itemId == InventoryConstants.ItemGold)
            {
                if (count > s.Money)
                    return (Success: false, Duration: (short)0, UniqueId: 0);

                var gold = s.Trade.ExchangeItemList.Find(entry => entry.ItemId == InventoryConstants.ItemGold);
                if (gold != null)
                    gold.Count += count;
                else
                    s.Trade.ExchangeItemList.Add(new ExchangeItem { ItemId = itemId, Count = count });
                s.Money -= count;
                return (Success: true, Duration: (short)0, UniqueId: 0);
            }

            var srcPos = (byte)(InventoryConstants.SlotMax + pos);
            var slot = s.Inventory[srcPos];
            if (slot.ItemId != itemId || slot.Count < count || !slot.IsTradable)
                return (Success: false, Duration: (short)0, UniqueId: 0);

            var offer = new ExchangeItem
            {
                ItemId = itemId,
                Durability = slot.Durability,
                Count = count,
                Flag = slot.Flag,
                ExpiresAt = slot.ExpiresAt,
                Countable = itemData!.Countable != 0,
                UniqueId = slot.UniqueId,
                SrcPos = srcPos,
            };

            // More of a stack already on offer joins that offer.
            var existing = offer.Countable
                ? s.Trade.ExchangeItemList.Find(entry => entry.SrcPos == srcPos
                    && entry.ItemId == itemId
                    && entry.UniqueId == offer.UniqueId
                    && entry.Flag == offer.Flag
                    && entry.ExpiresAt == offer.ExpiresAt
                    && entry.Count + count <= InventoryConstants.MaxStackCount)
                : null;

            if (existing == null
                && s.Trade.ExchangeItemList.Count(entry => entry.ItemId != InventoryConstants.ItemGold)
                    >= ExchangePacketConstants.MaxOfferedItems)
                return (Success: false, Duration: (short)0, UniqueId: 0);

            if (existing != null)
                existing.Count += count;
            else
                s.Trade.ExchangeItemList.Add(offer);

            if (slot.Count == count)
                slot.Clear();
            else
                slot.Count -= (ushort)count;

            return (Success: true, Duration: offer.Durability, UniqueId: offer.UniqueId);
        });

        if (!outcome.Success)
        {
            await SendAddFailAsync(session);
            return;
        }

        await session.Client.SendPacket(ExchangePacketWriter.Result(
            ExchangePacketConstants.ExchangeAdd, ExchangePacketWriter.Succeeded));

        var rings = await characterSeal.DescribeAsync([new ItemSlot { ItemId = itemId, UniqueId = outcome.UniqueId }]);
        await target.Client.SendPacket(ExchangePacketWriter.ItemOffered(
            ExchangePacketConstants.ExchangeOtherAdd, itemId, count, outcome.Duration,
            outcome.UniqueId, rings.TryGetValue(outcome.UniqueId, out var ring) ? ring : null));
    }

    public async Task DecideAsync(UserSession session)
    {
        var target = sessionManager.GetByCharacterId(session.Trade.ExchangeUser);
        if (target == null || target.Hp <= 0 || session.Hp <= 0 || !ExchangePacketConstants.IsWithinTradeRange(session, target))
        {
            await exchangeLifecycleService.CancelAsync(session);
            return;
        }

        var outcome = DecideOutcome.Wait;
        var sessionMoneyAfter = 0;
        var targetMoneyAfter = 0;
        List<ExchangeItem>? itemsForSession = null;
        List<ExchangeItem>? itemsForTarget = null;
        var sessionItemCount = 0;
        var targetItemCount = 0;

        ExchangeReturn? returnedSession = null, returnedTarget = null;
        UserSession.WithBoth(session, target, (sa, sb) =>
        {
            if (sa.Trade.ExchangeUser != sb.CharacterId || sb.Trade.ExchangeUser != sa.CharacterId)
            {
                outcome = DecideOutcome.Stale;
                return;
            }

            if (!sa.Trade.ExchangeAccepted || !sb.Trade.ExchangeAccepted)
            {
                outcome = DecideOutcome.Early;
                return;
            }

            if (!sb.Trade.ExchangeOk)
            {
                sa.Trade.ExchangeOk = true;
                outcome = DecideOutcome.Wait;
                return;
            }

            if (!CheckExchange(sa, sb) || !CheckExchange(sb, sa))
            {
                outcome = DecideOutcome.Fail;
                returnedSession = sa.InitExchange(false);
                returnedTarget = sb.InitExchange(false);
                return;
            }

            itemsForSession = ExecuteExchange(sa, sb);
            itemsForTarget = ExecuteExchange(sb, sa);

            sessionMoneyAfter = sa.Money;
            targetMoneyAfter = sb.Money;
            sessionItemCount = sa.Trade.ExchangeItemList.Count;
            targetItemCount = sb.Trade.ExchangeItemList.Count;

            var coeffSession = gameDataService.GetCoefficient(sa.Class);
            var coeffTarget = gameDataService.GetCoefficient(sb.Class);
            if (coeffSession != null)
                sa.RecalculateStats(coeffSession, gameDataService);
            if (coeffTarget != null)
                sb.RecalculateStats(coeffTarget, gameDataService);

            outcome = DecideOutcome.Done;

            sa.CompleteExchange();
            sb.CompleteExchange();
        });

        switch (outcome)
        {
            case DecideOutcome.Wait:
                await target.Client.SendPacket(
                    ExchangePacketWriter.Sub(ExchangePacketConstants.ExchangeOtherDecide));
                break;

            case DecideOutcome.Fail:
                var fail = ExchangePacketWriter.Result(
                    ExchangePacketConstants.ExchangeDone, ExchangePacketWriter.Failed);
                await session.Client.SendPacket(fail);
                await target.Client.SendPacket(fail);
                await exchangeLifecycleService.SettleReturnAsync(session, returnedSession);
                await exchangeLifecycleService.SettleReturnAsync(target, returnedTarget);
                break;

            case DecideOutcome.Stale:
                await exchangeLifecycleService.CancelAsync(session);
                break;

            case DecideOutcome.Done:
                logger.LogInformation("Exchange completed between {Name} ({ItemCount} items) and {TargetName} ({TargetItemCount} items)",
                    session.Name, sessionItemCount, target.Name, targetItemCount);

                await SendDoneCapturedAsync(session, sessionMoneyAfter, itemsForSession!);
                await SendDoneCapturedAsync(target, targetMoneyAfter, itemsForTarget!);

                await userNotificationService.SendWeightChangeAsync(session);
                await userNotificationService.SendWeightChangeAsync(target);
                break;
        }
    }

    private enum DecideOutcome { Wait, Fail, Done, Stale, Early }

    private async Task SendDoneCapturedAsync(UserSession receiver, int money, List<ExchangeItem> items)
    {
        var rings = await characterSeal.DescribeAsync(items.Select(item => new ItemSlot { ItemId = item.ItemId, UniqueId = item.UniqueId }));
        var transferred = new List<ExchangePacketWriter.TransferredItem>(items.Count);
        foreach (var item in items)
        {
            transferred.Add(new ExchangePacketWriter.TransferredItem(
                item.DstPos, item.ItemId, (ushort)item.Count, item.Durability,
                item.UniqueId, rings.TryGetValue(item.UniqueId, out var ring) ? ring : null));
        }

        await receiver.Client.SendPacket(ExchangePacketWriter.Completed(
            ExchangePacketConstants.ExchangeDone, money, transferred));
    }

    private bool CheckExchange(UserSession receiver, UserSession giver)
    {
        var money = 0L;
        var totalWeight = (int)receiver.Stats.ItemWeight;
        byte freeSlots = 0;
        byte itemCount = 0;

        for (var i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            if (receiver.Inventory[i].IsEmpty)
                freeSlots++;
        }

        foreach (var item in giver.Trade.ExchangeItemList)
        {
            if (item.ItemId == InventoryConstants.ItemGold)
            {
                money += item.Count;
                if (receiver.Money + money > ExchangePacketConstants.CoinMax)
                    return false;
                continue;
            }

            var itemData = gameDataService.GetItem(item.ItemId);
            if (itemData == null)
                return false;

            totalWeight += itemData.Weight * item.Count;
            if (totalWeight > receiver.Stats.MaxWeight)
                return false;

            itemCount++;
        }

        return itemCount <= freeSlots;
    }

    // An item with no room on the receiver's side goes back to the giver.
    private List<ExchangeItem> ExecuteExchange(UserSession receiver, UserSession giver)
    {
        var delivered = new List<ExchangeItem>();
        foreach (var item in giver.Trade.ExchangeItemList)
        {
            if (item.ItemId == InventoryConstants.ItemGold)
            {
                receiver.Money += item.Count;
                continue;
            }

            var slot = ItemPlacement.FindForDelivery(
                receiver.Inventory, InventoryConstants.InventoryStart, InventoryConstants.HaveMax, item);
            if (slot < 0)
            {
                if (!giver.ReturnEscrowedItem(item))
                    logger.LogError("Exchange item {ItemId} x{Count} of {Name} found no room to arrive or return",
                        item.ItemId, item.Count, giver.Name);
                continue;
            }

            var destination = receiver.Inventory[slot];
            ItemPlacement.Put(destination, item);
            delivered.Add(new ExchangeItem
            {
                ItemId = destination.ItemId,
                Count = destination.Count,
                Durability = destination.Durability,
                UniqueId = destination.UniqueId,
                DstPos = (byte)(slot - InventoryConstants.InventoryStart),
            });
        }

        return delivered;
    }

    internal static bool IsTradableItem(ItemData? itemData, int itemId, byte pos)
        => itemData != null
        && pos < InventoryConstants.HaveMax
        && !itemData.IsUntradeable
        && (itemId < ExchangePacketConstants.ItemNoTrade
            || itemId >= ExchangePacketConstants.ItemNoTradeMax);

    private static async Task SendAddFailAsync(UserSession session)
    {
        await session.Client.SendPacket(ExchangePacketWriter.Result(
            ExchangePacketConstants.ExchangeAdd, ExchangePacketWriter.Failed));
    }
}
