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
        if (session.Hp <= 0 || session.Trade.IsMerchanting)
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
            || target.Trade.IsMerchanting
            || target.ZoneId != session.ZoneId
            || target.Hp <= 0
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
        if (!session.Trade.IsTrading || session.Hp <= 0 || session.Trade.IsMerchanting
            || session.Trade.AskedForExchange)
            return;

        var agreed = packet.ReadByte();
        var target = sessionManager.GetByCharacterId(session.Trade.ExchangeUser);
        if (target == null)
        {
            session.Trade.ExchangeUser = -1;
            return;
        }

        if (agreed == 0
            || target.Hp <= 0
            || target.Trade.IsMerchanting
            || target.CharacterId == session.CharacterId
            || target.AccountId == session.AccountId
            || !ExchangePacketConstants.IsWithinTradeRange(session, target)
            || (target.Nation != session.Nation
                && !ZoneRules.Allows(session.ZoneId, ZoneFlags.TradeOtherNation)))
        {
            session.Trade.ExchangeUser = -1;
            target.Trade.ExchangeUser = -1;
            agreed = 0;
        }
        else
        {
            session.InitExchange(true);
            target.InitExchange(true);
        }

        await target.Client.SendPacket(ExchangePacketWriter.Result(
            ExchangePacketConstants.ExchangeAgree, agreed));
    }

    public async Task CancelAsync(UserSession session, bool isOnDeath = false)
    {
        if (!session.Trade.IsTrading || (!isOnDeath && session.Hp <= 0))
            return;

        var target = sessionManager.GetByCharacterId(session.Trade.ExchangeUser);
        session.InitExchange(false);
        logger.LogInformation("Trade cancelled for {Name}", session.Name);

        if (target != null)
        {
            target.InitExchange(false);
            await SendCancelAsync(target);
        }
    }

    private static async Task SendCancelAsync(UserSession session)
    {
        await session.Client.SendPacket(
            ExchangePacketWriter.Sub(ExchangePacketConstants.ExchangeCancel));
    }
}
