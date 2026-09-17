using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface INpcLifecycleService
{
    Task DespawnAsync(NpcInstance npc);
}

public sealed class NpcLifecycleService(
    SessionManager sessionManager,
    ILogger<NpcLifecycleService> logger) : INpcLifecycleService
{
    public async Task DespawnAsync(NpcInstance npc)
    {
        if (npc.IsDead)
            return;

        npc.WithLock(target =>
        {
            target.Hp = 0;
            target.DeathTimeTicks = DateTime.UtcNow.Ticks;
            target.State = NpcState.Dead;
            target.TargetUserId = 0;
            target.IsMoving = false;
            target.WakeTicks = 0;
        });

        sessionManager.Regions.MarkNpcIdle(npc);
        await sessionManager.Regions.BroadcastFromNpc(npc, DeathPacketWriter.NpcDeath(npc.UniqueId));

        logger.LogDebug(
            "NPC {NpcId}/{UniqueId} despawned in zone {Zone}; back in {Delay}ms",
            npc.NpcId, npc.UniqueId, npc.ZoneId, npc.RespawnDelayMs);
    }
}
