using LibreKO.Game.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class PetSatisfactionTickService(
    SessionManager sessionManager,
    IPetPacketCoordinator petPacketCoordinator,
    ILogger<PetSatisfactionTickService> logger) : BackgroundService
{
    private const int TickSeconds = 60;
    private const short DecayPerTick = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Pet satisfaction tick service started ({Interval}s decay -{Amount})",
            TickSeconds, DecayPerTick);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(TickSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Pet satisfaction tick failed");
            }
        }
    }

    private async Task TickAsync()
    {
        foreach (var session in sessionManager.GetAll())
        {
            var pet = session.Pet;
            if (pet == null) continue;

            var newSat = (short)Math.Max(0, pet.Satisfaction - DecayPerTick);
            pet.Satisfaction = newSat;

            if (newSat == 0)
            {
                logger.LogInformation("Pet of {Name} starved (satisfaction 0) — dismissed", session.Name);
                await petPacketCoordinator.SendDeathAsync(session);
                session.Pet = null;
                continue;
            }

            await petPacketCoordinator.SendSatisfactionUpdateAsync(session);
        }
    }
}
