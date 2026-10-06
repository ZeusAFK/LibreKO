using LibreKO.Game.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class SpecialAuctionTickService(
    ISpecialAuctionService specialAuctionService,
    ILogger<SpecialAuctionTickService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextPrune = DateTime.UtcNow;
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await specialAuctionService.SettleDueAsync();
                if (DateTime.UtcNow >= nextPrune)
                {
                    await specialAuctionService.PruneAsync();
                    nextPrune = DateTime.UtcNow + PruneInterval;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Akara's Altar tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
