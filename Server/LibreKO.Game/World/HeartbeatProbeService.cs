using LibreKO.Common.Infrastructure.Network;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public class HeartbeatProbeService(
    SessionManager sessionManager,
    ILogger<HeartbeatProbeService> logger) : BackgroundService
{
    private const int IntervalSeconds = 14;
    private const int ProbeDataLength = 16;
    private const byte HeartbeatOpcode = 0x02;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Heartbeat probe service started ({Interval}s, {Bytes}B random data)",
            IntervalSeconds, ProbeDataLength);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (sessionManager.OnlineCount == 0) continue;

                var data = new byte[ProbeDataLength];
                Random.Shared.NextBytes(data);

                var pkt = HeartbeatPacketWriter.Probe(HeartbeatOpcode, data);

                await sessionManager.BroadcastToAll(pkt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Heartbeat probe tick failed");
            }
        }
    }
}
