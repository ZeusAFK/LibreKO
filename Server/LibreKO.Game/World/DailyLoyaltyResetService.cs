using LibreKO.Common.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class DailyLoyaltyResetService(
    IServiceProvider serviceProvider,
    SessionManager sessionManager,
    ILogger<DailyLoyaltyResetService> logger) : BackgroundService
{
    private const int IntervalSeconds = 3600;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Daily-loyalty reset service started ({Interval}s tick)", IntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Daily-loyalty reset tick failed");
            }
        }
    }

    private async Task TickAsync()
    {
        // 1. Persist online sessions' DailyLoyalty back to DB before bulk-reset, so the
        //    DB row reflects the current accumulated value at reset time.
        foreach (var session in sessionManager.GetAll())
            session.DailyLoyalty = 0;

        // 2. Bulk reset all DB rows.
        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var rowsAffected = await repo.ResetDailyLoyaltyAll();

        logger.LogInformation("Daily PK loyalty reset: {Rows} DB rows + {Online} online sessions",
            rowsAffected, sessionManager.OnlineCount);
    }
}
