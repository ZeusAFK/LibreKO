using LibreKO.Game.Scripting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public sealed class QuestAvailabilityService(
    SessionManager sessions,
    IQuestDefinitionSource quests,
    ILogger<QuestAvailabilityService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessTickAsync();
    }

    public async Task ProcessTickAsync()
    {
        foreach (var session in sessions.GetAll())
        {
            if (session.Quest.ViewZone != session.ZoneId || session.Hp <= 0)
                continue;
            try
            {
                await quests.SendViewsAsync(session, changesOnly: true);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not refresh quest availability for {CharacterId}", session.CharacterId);
            }
        }
    }
}
