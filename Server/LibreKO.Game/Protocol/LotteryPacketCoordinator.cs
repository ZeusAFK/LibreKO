using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface ILotteryPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class LotteryPacketCoordinator(
    SessionManager sessionManager,
    ILotteryService lotteryService) : ILotteryPacketCoordinator
{
    public const byte SubReqState = 1;
    public const byte SubReqJoin = 2;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case SubReqState:
                await lotteryService.SyncPlayerAsync(session);
                break;

            case SubReqJoin:
                var res = await lotteryService.BuyTicketAsync(session);
                await session.Client.SendPacket(LotteryPacketWriter.JoinAck(res.Success, res.Message, res.MyTickets, res.TotalTickets));
                break;
        }
    }
}
