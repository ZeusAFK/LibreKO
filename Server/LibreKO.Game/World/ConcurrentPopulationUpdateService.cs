using LibreKO.Common.Domain.Services;
using LibreKO.Game.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public class ConcurrentPopulationUpdateService(
    IServiceProvider serviceProvider,
    SessionManager sessionManager,
    IOptions<GameServerSettings> settings,
    ILogger<ConcurrentPopulationUpdateService> logger) : BackgroundService
{
    private const int IntervalSeconds = 120;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverId = settings.Value.ServerId;
        logger.LogInformation(
            "Concurrent-population update service started (id={ServerId}, {Interval}s tick)",
            serverId, IntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(serverId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Concurrent-population update tick failed");
            }
        }
    }

    private async Task TickAsync(int serverId)
    {
        var count = sessionManager.OnlineCount;

        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IServerRepository>();
        await repo.UpdateOnlinePlayersAsync(serverId, count);

        logger.LogDebug("Concurrent-population update: server {Id} = {Count} online", serverId, count);
    }
}
