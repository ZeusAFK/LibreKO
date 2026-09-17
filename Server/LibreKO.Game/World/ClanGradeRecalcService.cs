using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class ClanGradeRecalcService(
    IServiceProvider serviceProvider,
    SessionManager sessionManager,
    ILogger<ClanGradeRecalcService> logger) : BackgroundService
{
    private const int IntervalSeconds = 3600;

    public const int Grade1Points = 720_000;
    public const int Grade2Points = 360_000;
    public const int Grade3Points = 144_000;
    public const int Grade4Points = 72_000;

    private int lastRunDay = -1;

    public static byte GetGradeFromPoints(int points) => points switch
    {
        >= Grade1Points => 1,
        >= Grade2Points => 2,
        >= Grade3Points => 3,
        >= Grade4Points => 4,
        _ => 5,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Clan-grade recalc service started ({Interval}s tick)", IntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var today = DateTime.UtcNow.DayOfYear;
                if (today == lastRunDay) continue;
                lastRunDay = today;
                await RunAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Clan-grade recalc tick failed");
            }
        }
    }

    private async Task RunAsync()
    {
        var clans = sessionManager.Knights.GetAll().ToList();
        if (clans.Count == 0) return;

        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();

        var updated = 0;
        var demoted = 0;
        foreach (var clan in clans)
        {
            var newGrade = clan.Flag >= (byte)ClanType.Accredited5 ? (byte)1 : GetGradeFromPoints(clan.Points);
            var needsDemote = clan.Flag == (byte)ClanType.Promoted && newGrade > 3;
            var oldGrade = clan.Grade;

            if (newGrade == oldGrade && !needsDemote) continue;

            clan.Grade = newGrade;
            if (needsDemote)
            {
                clan.Flag = (byte)ClanType.Training;
                clan.Cape = ClanRules.NoCape;
                clan.CapeR = 0;
                clan.CapeG = 0;
                clan.CapeB = 0;
                demoted++;
            }
            updated++;

            await repo.UpdateAsync(clan);
        }

        logger.LogInformation(
            "Clan-grade recalc: {Total} clans processed, {Updated} grades changed, {Demoted} demoted",
            clans.Count, updated, demoted);
    }
}
