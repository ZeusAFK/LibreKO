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

        if (itemId != InventoryConstants.ItemGold && !IsTradableItem(itemData, itemId, pos))
        {
            await SendAddFailAsync(session);
            return;
        }

        var outcome = session.WithLock(s =>
        {
            if (s.Trade.ExchangeOk)
                return (Success: false, Duration: (short)0);

            var addNew = true;
            var duration = (short)0;

            if (itemId == InventoryConstants.ItemGold)
            {
                if (count <= 0 || count > s.Money)
                    return (Success: false, Duration: (short)0);

                var existing = s.Trade.ExchangeItemList.Find(entry => entry.ItemId == InventoryConstants.ItemGold);
                if (existing != null)
                {
                    existing.Count += count;
                    addNew = false;
                }

                s.Money -= count;
            }
            else
            {
                var slot = s.Inventory[InventoryConstants.SlotMax + pos];
                if (slot.ItemId != itemId || slot.Count < count || !slot.IsTradable)
                    return (Success: false, Duration: (short)0);

                duration = slot.Durability;

                if (itemData!.Countable != 0)
                {
                    var existing = s.Trade.ExchangeItemList.Find(entry => entry.ItemId == itemId);
                    if (existing != null)
                    {
                        existing.Count += count;
                        addNew = false;
                    }
                }

                slot.Count -= (ushort)count;
            }

            var hasGold = s.Trade.ExchangeItemList.Exists(entry => entry.ItemId == InventoryConstants.ItemGold);
            if (s.Trade.ExchangeItemList.Count > (hasGold ? 13 : 12))
                return (Success: false, Duration: (short)0);

            if (addNew)
            {
                s.Trade.ExchangeItemList.Add(new ExchangeItem
                {
                    ItemId = itemId,
                    Durability = duration,
                    Count = count,
                    SrcPos = (byte)(InventoryConstants.SlotMax + pos)
                });
            }

            return (Success: true, Duration: duration);
        });

        if (!outcome.Success)
        {
            await SendAddFailAsync(session);
            return;
        }

        await session.Client.SendPacket(ExchangePacketWriter.Result(
            ExchangePacketConstants.ExchangeAdd, ExchangePacketWriter.Succeeded));

        await target.Client.SendPacket(ExchangePacketWriter.ItemOffered(
            ExchangePacketConstants.ExchangeOtherAdd, itemId, count, outcome.Duration));
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

        UserSession.WithBoth(session, target, (sa, sb) =>
        {
            if (!sb.Trade.ExchangeOk)
            {
                sa.Trade.ExchangeOk = true;
                outcome = DecideOutcome.Wait;
                return;
            }

            if (!CheckExchange(sa, sb) || !CheckExchange(sb, sa))
            {
                outcome = DecideOutcome.Fail;
                sa.InitExchange(false);
                sb.InitExchange(false);
                return;
            }

            ExecuteExchange(sa, sb);
            ExecuteExchange(sb, sa);

            sessionMoneyAfter = sa.Money;
            targetMoneyAfter = sb.Money;
            itemsForSession = [.. sb.Trade.ExchangeItemList.Where(e => e.ItemId != InventoryConstants.ItemGold)];
            itemsForTarget = [.. sa.Trade.ExchangeItemList.Where(e => e.ItemId != InventoryConstants.ItemGold)];
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

    private enum DecideOutcome { Wait, Fail, Done }

    private static async Task SendDoneCapturedAsync(UserSession receiver, int money, List<ExchangeItem> items)
    {
        var transferred = new List<ExchangePacketWriter.TransferredItem>(items.Count);
        foreach (var item in items)
        {
            transferred.Add(new ExchangePacketWriter.TransferredItem(
                item.DstPos, item.ItemId, (ushort)item.Count, item.Durability));
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

    private void ExecuteExchange(UserSession receiver, UserSession giver)
    {
        foreach (var item in giver.Trade.ExchangeItemList)
        {
            if (item.ItemId == InventoryConstants.ItemGold)
            {
                receiver.Money += item.Count;
                continue;
            }

            var slot = receiver.FindSlotForItem(item.ItemId, gameDataService, (ushort)item.Count);
            if (slot < 0)
            {
                giver.Inventory[item.SrcPos].Count += (ushort)item.Count;
                continue;
            }

            var dst = receiver.Inventory[slot];
            var src = giver.Inventory[item.SrcPos];

            dst.ItemId = item.ItemId;
            dst.Count += (ushort)item.Count;
            if (dst.Count > 9999)
                dst.Count = 9999;
            dst.Durability = item.Durability;

            item.DstPos = (byte)(slot - InventoryConstants.SlotMax);

            if (src.Count == 0)
                src.Clear();
        }
    }

    internal static bool IsTradableItem(ItemData? itemData, int itemId, byte pos)
        => itemData != null
        && pos < InventoryConstants.HaveMax
        && itemData.Race != ExchangePacketConstants.RaceUntradeable
        && (itemId < ExchangePacketConstants.ItemNoTrade
            || itemId >= ExchangePacketConstants.ItemNoTradeMax);

    private static async Task SendAddFailAsync(UserSession session)
    {
        await session.Client.SendPacket(ExchangePacketWriter.Result(
            ExchangePacketConstants.ExchangeAdd, ExchangePacketWriter.Failed));
    }
}
