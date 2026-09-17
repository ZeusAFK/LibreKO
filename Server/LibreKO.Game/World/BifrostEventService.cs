using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public interface IBifrostEventService
{
    long RemainingSecs { get; }

    bool IsActive { get; }

    void Start(int? monumentMinutes = null);

    void Close();
}

public class BifrostEventService(
    SessionManager sessionManager,
    ILogger<BifrostEventService> logger) : BackgroundService, IBifrostEventService
{
    private const ushort ZoneBifrost = (ushort)ZoneId.Bifrost;
    private const ushort ZoneRonarkLand = (ushort)ZoneId.RonarkLand;

    private const int DefaultMonumentMinutes = 120;

    private readonly object stateLock = new();
    private long remainingSecs;
    private bool isActive;

    public long RemainingSecs
    {
        get { lock (stateLock) return remainingSecs; }
    }

    public bool IsActive
    {
        get { lock (stateLock) return isActive; }
    }

    public void Start(int? monumentMinutes = null)
    {
        lock (stateLock)
        {
            if (isActive)
            {
                logger.LogWarning("Bifrost start requested but event already active; ignored");
                return;
            }

            var minutes = monumentMinutes ?? DefaultMonumentMinutes;
            remainingSecs = (long)minutes * 60;
            isActive = true;
            logger.LogInformation("Bifrost event started: {Minutes}min ({Secs}s)", minutes, remainingSecs);
        }

        _ = BroadcastRemainingAsync();
    }

    public void Close()
    {
        bool wasActive;
        lock (stateLock)
        {
            wasActive = isActive;
            isActive = false;
            remainingSecs = 0;
        }
        if (wasActive)
        {
            logger.LogInformation("Bifrost event closed by GM");
            _ = BroadcastRemainingAsync();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Bifrost tick error");
            }
        }
    }

    private async Task TickAsync()
    {
        bool justExpired = false;
        lock (stateLock)
        {
            if (!isActive)
                return;

            if (remainingSecs > 0)
            {
                remainingSecs--;
                if (remainingSecs == 0)
                {
                    // Phase 2 will switch to farming; for now we reset.
                    isActive = false;
                    justExpired = true;
                }
            }
        }

        if (justExpired)
        {
            logger.LogInformation("Bifrost event ended in draw (monument phase elapsed)");
            await BroadcastRemainingAsync();
        }
    }

    private Task BroadcastRemainingAsync()
    {
        var remaining = RemainingSecs;
        var pkt = BifrostPacketWriter.Remaining(
            TempleSubOpcode.BifrostRemaining, (int)Math.Min(remaining, int.MaxValue));

        foreach (var session in sessionManager.GetAll())
        {
            if (session.ZoneId != ZoneBifrost && session.ZoneId != ZoneRonarkLand)
                continue;
            try
            {
                _ = session.Client.SendPacket(pkt);
            }
            catch
            {
                // best-effort broadcast
            }
        }
        return Task.CompletedTask;
    }
}
