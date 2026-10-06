using LibreKO.Game.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class MarketPricePruneService(
    IMarketPriceService marketPriceService,
    ILogger<MarketPricePruneService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await marketPriceService.PruneAsync(DateOnly.FromDateTime(DateTime.UtcNow));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Market price pruning failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
