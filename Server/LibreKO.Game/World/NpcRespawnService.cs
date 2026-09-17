using LibreKO.Common.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class NpcRespawnService(SessionManager sessionManager, ILogger<NpcRespawnService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NPC respawn service started");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var now = DateTime.UtcNow.Ticks;
                var npcsToRespawn = sessionManager.Regions.GetDeadNpcsReadyToRespawn(now).ToList();

                foreach (var npc in npcsToRespawn)
                {
                    npc.Respawn();

                    if (!NpcWorldFilter.ShouldSpawnNormally(npc))
                        continue;

                    // Update region if spawn position is different from death position
                    sessionManager.Regions.UpdateNpcRegion(npc);

                    // Broadcast respawn to nearby players
                    var pkt = Protocol.NpcPacketMapper.BuildInOutPacket(npc, InOutType.In);
                    await sessionManager.Regions.BroadcastFromNpc(npc, pkt);
                }

                if (npcsToRespawn.Count > 0)
                    logger.LogDebug("Respawned {Count} NPCs", npcsToRespawn.Count);

                // Cleanup expired loot bundles
                sessionManager.Regions.CleanupExpiredBundles();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in NPC respawn tick");
            }
        }
    }

}
