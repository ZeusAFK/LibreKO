using LibreKO.Game.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public class AutoSaveService(
    SessionManager sessionManager,
    ICharacterStatePersister characterStatePersister,
    IOptions<GameServerSettings> settings,
    ILogger<AutoSaveService> logger) : BackgroundService
{
    // Cap concurrent saves so a full-population auto-save can't drain the DB connection
    // pool. Each SaveAsync uses its own scope/DbContext, so running them in parallel is safe.
    private const int MaxConcurrentSaves = 32;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delaySeconds = settings.Value.Player.AutoSaveDelaySeconds;
        if (delaySeconds <= 0) delaySeconds = 900;

        logger.LogInformation("Auto-save service started ({Delay}s interval)", delaySeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(delaySeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var sessions = sessionManager.GetAll().Where(s => !s.IsBot && s.Hp > 0).ToList(); // skip dead players and bots
                if (sessions.Count == 0) continue;

                var saved = 0;
                await Parallel.ForEachAsync(
                    sessions,
                    new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentSaves, CancellationToken = stoppingToken },
                    async (session, ct) =>
                    {
                        try
                        {
                            if (await characterStatePersister.SaveAsync(session, ct))
                                Interlocked.Increment(ref saved);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogWarning(ex, "Failed to auto-save character {CharId}", session.CharacterId);
                        }
                    });

                logger.LogInformation("Auto-saved {Count}/{Total} characters", saved, sessions.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in auto-save tick");
            }
        }
    }
}
