using System.Diagnostics;
using LibreKO.Game.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public class NpcAiService(
    SessionManager sessionManager,
    INpcAiBehaviorService npcAiBehaviorService,
    IOptions<GameServerSettings> settings,
    ILogger<NpcAiService> logger) : BackgroundService
{
    public const int TickMs = 250;
    private readonly int _maxWorkerCount = Math.Max(
        1,
        settings.Value.Global.ThreadPoolSize > 0
            ? settings.Value.Global.ThreadPoolSize
            : Environment.ProcessorCount);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NPC AI service started ({TickMs}ms tick, {WorkerCount} workers)", TickMs, _maxWorkerCount);

        var sw = new Stopwatch();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMs));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                sw.Restart();
                var nowTicks = DateTime.UtcNow.Ticks;
                var activeNpcs = sessionManager.GetActiveAiNpcsSnapshot();
                if (activeNpcs.Length == 0)
                    continue;

                await ProcessActiveNpcsAsync(activeNpcs, nowTicks, stoppingToken);
                sw.Stop();

                if (sw.ElapsedMilliseconds > TickMs)
                    logger.LogWarning("NPC AI tick overrun: {ElapsedMs}ms for {NpcCount} NPCs", sw.ElapsedMilliseconds, activeNpcs.Length);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in NPC AI tick");
            }
        }
    }

    private async Task ProcessActiveNpcsAsync(NpcInstance[] activeNpcs, long nowTicks, CancellationToken stoppingToken)
    {
        var workerCount = Math.Min(_maxWorkerCount, activeNpcs.Length);
        if (workerCount <= 1)
        {
            await ProcessNpcRangeAsync(activeNpcs, 0, activeNpcs.Length, nowTicks, stoppingToken);
            return;
        }

        var chunkSize = (activeNpcs.Length + workerCount - 1) / workerCount;
        var tasks = new List<Task>(workerCount);

        for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            var start = workerIndex * chunkSize;
            if (start >= activeNpcs.Length)
                break;

            var end = Math.Min(start + chunkSize, activeNpcs.Length);
            tasks.Add(Task.Run(() => ProcessNpcRangeAsync(activeNpcs, start, end, nowTicks, stoppingToken), stoppingToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProcessNpcRangeAsync(NpcInstance[] activeNpcs, int start, int end, long nowTicks, CancellationToken stoppingToken)
    {
        for (var index = start; index < end; index++)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var npc = activeNpcs[index];
            try
            {
                RegenerateHealth(npc, nowTicks);
                await npcAiBehaviorService.ProcessNpcAsync(npc, nowTicks);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing NPC {NpcId}/{UniqueId}", npc.NpcId, npc.UniqueId);
            }
        }
    }

    private static void RegenerateHealth(NpcInstance npc, long nowTicks)
    {
        if (npc.Hp >= npc.MaxHp
            || npc.Hp <= 0
            || nowTicks - npc.LastRegenTicks <= TimeSpan.TicksPerSecond * 10)
        {
            return;
        }

        npc.Hp = Math.Min(npc.MaxHp, npc.Hp + npc.MaxHp / 20);
        npc.LastRegenTicks = nowTicks;
    }
}
