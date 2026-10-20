using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class SlaveMerchantTickService(
    ISlaveMerchantService slaveMerchants,
    ILogger<SlaveMerchantTickService> logger) : BackgroundService
{
    private const int TickSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await slaveMerchants.TickAsync(DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Slave merchant tick failed");
            }
        }
    }
}
