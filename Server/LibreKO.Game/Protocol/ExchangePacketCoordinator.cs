using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IExchangePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task CancelAsync(UserSession session, bool isOnDeath = false);
}

public class ExchangePacketCoordinator(
    SessionManager sessionManager,
    IExchangeLifecycleService exchangeLifecycleService,
    IExchangeTransferService exchangeTransferService,
    ILogger<ExchangePacketCoordinator> logger) : IExchangePacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var opcode = packet.ReadByte();
        switch (opcode)
        {
            case ExchangePacketConstants.ExchangeRequest:
                await exchangeLifecycleService.RequestAsync(session, packet);
                break;

            case ExchangePacketConstants.ExchangeAgree:
                await exchangeLifecycleService.AgreeAsync(session, packet);
                break;

            case ExchangePacketConstants.ExchangeAdd:
                await exchangeTransferService.AddAsync(session, packet);
                break;

            case ExchangePacketConstants.ExchangeDecide:
                await exchangeTransferService.DecideAsync(session);
                break;

            case ExchangePacketConstants.ExchangeCancel:
                await exchangeLifecycleService.CancelAsync(session);
                break;

            default:
                logger.LogDebug("Unhandled exchange sub-opcode {Opcode} from {Name}", opcode, session.Name);
                break;
        }
    }

    public async Task CancelAsync(UserSession session, bool isOnDeath = false)
    {
        await exchangeLifecycleService.CancelAsync(session, isOnDeath);
    }
}
