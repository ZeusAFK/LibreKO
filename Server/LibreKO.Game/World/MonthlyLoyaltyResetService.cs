using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class MonthlyLoyaltyResetService(
    SessionManager sessionManager,
    IServiceProvider serviceProvider,
    ILogger<MonthlyLoyaltyResetService> logger) : BackgroundService
{
    private int _lastResetMonth;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _lastResetMonth = DateTime.UtcNow.Month;
        logger.LogInformation("Monthly loyalty reset service started");

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var now = DateTime.UtcNow;
                if (now.Day == 1 && now.Month != _lastResetMonth)
                {
                    _lastResetMonth = now.Month;
                    await ResetMonthlyLoyalty();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in monthly loyalty reset");
            }
        }
    }

    private async Task ResetMonthlyLoyalty()
    {
        // Reset all online sessions
        foreach (var session in sessionManager.GetAll())
            session.MonthlyLoyalty = 0;

        // Reset all characters in DB
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var characters = await db.Characters.Where(c => c.LoyaltyMonthly > 0).ToListAsync();
        foreach (var c in characters)
            c.LoyaltyMonthly = 0;
        await db.SaveChangesAsync();

        logger.LogInformation("Monthly loyalty reset completed for all characters");
    }
}
