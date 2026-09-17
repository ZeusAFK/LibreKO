using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public sealed class GracefulShutdownService(
    SessionManager sessionManager,
    ISessionTerminationService sessionTerminationService,
    IAccountLockService accountLockService,
    ILogger<GracefulShutdownService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var sessions = sessionManager.GetAll().ToArray();
        if (sessions.Length > 0)
        {
            logger.LogInformation("Graceful shutdown: logging out {SessionCount} active session(s)", sessions.Length);

            foreach (var session in sessions)
            {
                try
                {
                    await sessionTerminationService.LogoutAsync(session.Client, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Graceful shutdown failed to log out {Name} (CharId={CharId})",
                        session.Name,
                        session.CharacterId);
                }
            }
        }

        await accountLockService.ClearOwnClaimsAsync();
    }
}
