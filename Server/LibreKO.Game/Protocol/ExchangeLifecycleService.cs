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
    Task CancelAsync(UserSession session, bool isOnDeath = false);
}

public class ExchangeLifecycleService(SessionManager sessionManager,
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
        // A refused request still hands back anything offered into escrow before the answer.
        if (target == null)
        {
            LogUnplaced(session, session.WithLock(s => s.InitExchange(false)));
            return;
        }

        if (agreed == 0
            || CannotStartTrade(session)
            || CannotStartTrade(target)
            || target.CharacterId == session.CharacterId
            || target.AccountId == session.AccountId
            || !ExchangePacketConstants.IsWithinTradeRange(session, target)
            || (target.Nation != session.Nation
                && !ZoneRules.Allows(session.ZoneId, ZoneFlags.TradeOtherNation)))
        {
            LogUnplaced(session, session.WithLock(s => s.InitExchange(false)));
            if (target.Trade.ExchangeUser == session.CharacterId)
                LogUnplaced(target, target.WithLock(t => t.InitExchange(false)));
            agreed = 0;
        }
        else
        {
            ExchangeReturn? returnedSession = null, returnedTarget = null;
            var paired = false;
            UserSession.WithBoth(session, target, (s, t) =>
            {
                paired = s.Trade.ExchangeUser == t.CharacterId && t.Trade.ExchangeUser == s.CharacterId;
                if (!paired)
                    return;
                returnedSession = s.InitExchange(true);
                returnedTarget = t.InitExchange(true);
            });
            LogUnplaced(session, returnedSession);
            LogUnplaced(target, returnedTarget);
            if (!paired)
                return;
        }

        await target.Client.SendPacket(ExchangePacketWriter.Result(
            ExchangePacketConstants.ExchangeAgree, agreed));
    }

    public async Task CancelAsync(UserSession session, bool isOnDeath = false)
    {
        if (!session.Trade.IsTrading || (!isOnDeath && session.Hp <= 0))
            return;

        var target = sessionManager.GetByCharacterId(session.Trade.ExchangeUser);
        LogUnplaced(session, session.WithLock(s => s.InitExchange(false)));
        logger.LogInformation("Trade cancelled for {Name}", session.Name);

        if (target != null && target.Trade.ExchangeUser == session.CharacterId)
        {
            LogUnplaced(target, target.WithLock(t => t.InitExchange(false)));
            await SendCancelAsync(target);
        }
    }

    // Dead, keeping or setting up a stall, or gathering: such a player can neither ask for a
    // trade nor take one up, so no one is ever trading and selling or gathering at once.
    private static bool CannotStartTrade(UserSession session) =>
        session.Hp <= 0 || session.Trade.IsMerchanting || session.Trade.IsMerchantPreparing || session.IsGathering;

    private void LogUnplaced(UserSession session, ExchangeReturn? returned) =>
        returned?.LogLosses(logger, session.Name);

    private static async Task SendCancelAsync(UserSession session)
    {
        await session.Client.SendPacket(
            ExchangePacketWriter.Sub(ExchangePacketConstants.ExchangeCancel));
    }
}
