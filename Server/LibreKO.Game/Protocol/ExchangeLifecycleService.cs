using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IExchangeLifecycleService
{
    Task RequestAsync(UserSession session, Packet packet);
    Task AgreeAsync(UserSession session, Packet packet);
    Task CancelAsync(UserSession session, bool isOnDeath = false, bool notifySelf = true);
    Task SettleReturnAsync(UserSession session, ExchangeReturn? returned);
}

public class ExchangeLifecycleService(SessionManager sessionManager,
    IUserNotificationService userNotificationService,
    ILogger<ExchangeLifecycleService> logger) : IExchangeLifecycleService
{
    public async Task RequestAsync(UserSession session, Packet packet)
    {
        if (CannotStartTrade(session))
        {
            await SendCancelAsync(session);
            return;
        }

        if (session.Trade.IsTrading)
        {
            await CancelAsync(session);
            return;
        }

        var destId = packet.ReadInt();
        var target = sessionManager.GetByCharacterId(destId);
        if (target == null
            || target.Trade.IsTrading
            || CannotStartTrade(target)
            || target.ZoneId != session.ZoneId
            || target.CharacterId == session.CharacterId
            || target.AccountId == session.AccountId
            || !ExchangePacketConstants.IsWithinTradeRange(session, target)
            || (target.Nation != session.Nation
                && !ZoneRules.Allows(session.ZoneId, ZoneFlags.TradeOtherNation)))
        {
            await SendCancelAsync(session);
            return;
        }

        session.Trade.ExchangeUser = target.CharacterId;
        session.Trade.AskedForExchange = true;
        target.Trade.ExchangeUser = session.CharacterId;
        logger.LogInformation("Trade requested by {Name} to {TargetId}", session.Name, target.CharacterId);

        await target.Client.SendPacket(ExchangePacketWriter.Partner(
            ExchangePacketConstants.ExchangeRequest, session.CharacterId));
    }

    public async Task AgreeAsync(UserSession session, Packet packet)
    {
        if (!session.Trade.IsTrading || session.Trade.AskedForExchange)
            return;

        var agreed = packet.ReadByte();
        var target = sessionManager.GetByCharacterId(session.Trade.ExchangeUser);
        if (target == null)
        {
            await SettleReturnAsync(session, session.WithLock(s => s.InitExchange(false)));
            return;
        }

        ExchangeReturn? returnedSession = null, returnedTarget = null;
        var refusedAccept = false;
        if (agreed == 0
            || CannotStartTrade(session)
            || CannotStartTrade(target)
            || target.CharacterId == session.CharacterId
            || target.AccountId == session.AccountId
            || !ExchangePacketConstants.IsWithinTradeRange(session, target)
            || (target.Nation != session.Nation
                && !ZoneRules.Allows(session.ZoneId, ZoneFlags.TradeOtherNation)))
        {
            returnedSession = session.WithLock(s => s.InitExchange(false));
            returnedTarget = target.WithLock(t => t.Trade.ExchangeUser == session.CharacterId ? t.InitExchange(false) : null);
            refusedAccept = agreed != 0;
            agreed = 0;
        }
        else
        {
            var paired = false;
            UserSession.WithBoth(session, target, (s, t) =>
            {
                paired = s.Trade.ExchangeUser == t.CharacterId && t.Trade.ExchangeUser == s.CharacterId;
                if (!paired)
                    return;
                returnedSession = s.InitExchange(true);
                returnedTarget = t.InitExchange(true);
            });
            if (!paired)
            {
                var stale = session.WithLock(s => s.Trade.IsTrading ? s.InitExchange(false) : null);
                await SendCancelAsync(session);
                await SettleReturnAsync(session, stale);
                return;
            }
        }

        await target.Client.SendPacket(ExchangePacketWriter.Result(
            ExchangePacketConstants.ExchangeAgree, agreed));
        if (refusedAccept)
            await SendCancelAsync(session);
        await SettleReturnAsync(session, returnedSession);
        await SettleReturnAsync(target, returnedTarget);
    }

    // notifySelf is false only for the player's own cancel; that client has closed its window already.
    public async Task CancelAsync(UserSession session, bool isOnDeath = false, bool notifySelf = true)
    {
        if (!isOnDeath && session.Hp <= 0)
            return;

        // Checking and ending the trade is one locked step, so a decision that completes it is never also cancelled.
        var partnerId = -1;
        var returnedSession = session.WithLock(s =>
        {
            if (!s.Trade.IsTrading)
                return null;
            partnerId = s.Trade.ExchangeUser;
            return s.InitExchange(false);
        });
        if (returnedSession == null)
            return;

        logger.LogInformation("Trade cancelled for {Name}", session.Name);
        if (notifySelf)
            await SendCancelAsync(session);

        var target = sessionManager.GetByCharacterId(partnerId);
        var returnedTarget = target?.WithLock(t =>
            t.Trade.ExchangeUser == session.CharacterId ? t.InitExchange(false) : null);
        if (returnedTarget != null)
            await SendCancelAsync(target!);

        await SettleReturnAsync(session, returnedSession);
        if (target != null)
            await SettleReturnAsync(target, returnedTarget);
    }

    // Runs after the client knows the trade is over, to tell it where each offer really went.
    public async Task SettleReturnAsync(UserSession session, ExchangeReturn? returned)
    {
        if (returned == null)
            return;

        returned.LogLosses(logger, session.Name);
        foreach (var index in returned.BagSlots.Distinct())
        {
            var (itemId, count, durability) = session.WithLock(s =>
                (s.Inventory[index].ItemId, s.Inventory[index].Count, s.Inventory[index].Durability));
            await userNotificationService.SendStackChangeAsync(session, (byte)index, itemId, count, durability);
        }

        if (returned.ItemsToWarehouse > 0)
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation,
                $"{returned.ItemsToWarehouse} offered item(s) did not fit in your bag and went to your warehouse."));

        if (returned.GoldToWarehouse > 0)
        {
            await userNotificationService.SendGoldLossAsync(session, returned.GoldToWarehouse);
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation,
                $"{returned.GoldToWarehouse:N0} coins did not fit in your purse and went to your warehouse."));
        }
    }

    // A trade never overlaps death, a stall, its setup or gathering.
    private static bool CannotStartTrade(UserSession session) =>
        session.Hp <= 0 || session.Trade.IsMerchanting || session.Trade.IsMerchantPreparing || session.IsGathering;

    private static async Task SendCancelAsync(UserSession session)
    {
        await session.Client.SendPacket(
            ExchangePacketWriter.Sub(ExchangePacketConstants.ExchangeCancel));
    }
}
