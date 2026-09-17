using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public class MovementBroadcastService(
    SessionManager sessionManager,
    IOptions<GameServerSettings> settings,
    ILogger<MovementBroadcastService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hz = settings.Value.Global.MovementBroadcastHz;
        if (hz <= 0) hz = 10;
        var interval = TimeSpan.FromMilliseconds(1000.0 / hz);

        logger.LogInformation("Movement broadcast service started ({Hz} Hz)", hz);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                FlushMovers();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in movement broadcast tick");
            }
        }
    }

    public void FlushMovers()
    {
        foreach (var session in sessionManager.GetAll())
        {
            if (!session.MovePending)
                continue;
            session.MovePending = false;

            var result = MovementPacketWriter.Move(
                session.CharacterId, session.MoveOldWillX, session.MoveOldWillZ,
                session.MoveOldWillY, session.MoveOldSpeed, session.MoveOldEcho);

            _ = sessionManager.Regions.SendToRegion(session, result, excludeSender: false);
        }
    }
}
