using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IEventBoardPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class EventBoardPacketCoordinator(
    IAttendancePacketCoordinator attendance,
    IRoulettePacketCoordinator roulette,
    ILogger<EventBoardPacketCoordinator> logger) : IEventBoardPacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var sub = (EventBoardSubOpcode)packet.ReadByte();
        packet.ResetOffset();

        switch (sub)
        {
            case EventBoardSubOpcode.AttendanceBoard:
            case EventBoardSubOpcode.AttendanceClaim:
                await attendance.HandleAsync(client, packet);
                return;

            case EventBoardSubOpcode.RouletteOpen:
            case EventBoardSubOpcode.RouletteAck:
            case EventBoardSubOpcode.RouletteSpin:
            case EventBoardSubOpcode.RoulettePrizeList:
                await roulette.HandleAsync(client, packet);
                return;
        }

        logger.LogDebug("Event board sub-opcode {Sub} is not implemented", sub);
    }
}
