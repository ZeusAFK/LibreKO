using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class GenieTickService(
    SessionManager sessionManager,
    IGenieSystemPacketCoordinator genie,
    ILogger<GenieTickService> logger) : BackgroundService
{
    private const int TickSeconds = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Genie tick service started ({Interval}s)", TickSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Genie tick failed");
            }
        }
    }

    private async Task TickAsync()
    {
        foreach (var session in sessionManager.GetAll())
        {
            if (!session.GenieActive)
                continue;

            var remaining = session.GenieMinutes;
            if (remaining == 0)
            {
                await genie.StopAsync(session);
                continue;
            }

            await session.Client.SendPacket(GenieSystemPacketWriter.Remaining(remaining));
        }
    }
}
