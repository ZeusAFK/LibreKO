using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class ItemExpiryService(
    SessionManager sessionManager,
    ILogger<ItemExpiryService> logger) : BackgroundService
{
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Item expiry service started");

        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessTickAsync(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in item expiry tick");
            }
        }
    }

    public async Task ProcessTickAsync(long nowUnixSeconds)
    {
        foreach (var session in sessionManager.GetAll())
        {
            ItemExpiry.Sweep(session.Warehouse, nowUnixSeconds);
            ItemExpiry.Sweep(session.VipWarehouse, nowUnixSeconds);

            var cleared = ItemExpiry.Sweep(session.Inventory, nowUnixSeconds,
                InventoryConstants.InventoryStart, InventoryConstants.HaveMax);
            if (cleared.Count == 0)
                continue;

            var writer = new ItemCountChangePacketWriter();
            foreach (var slot in cleared)
                writer.Add((byte)slot, 0, 0, 0);
            await session.Client.SendPacket(writer.Build());
        }
    }
}
