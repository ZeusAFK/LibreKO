using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public class ItemExpiryService(
    SessionManager sessionManager,
    IItemEquipmentEffectService equipmentEffects,
    IUserNotificationService userNotifications,
    ILogger<ItemExpiryService> logger) : BackgroundService
{
    public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private long sweptUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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
        var since = sweptUntil;
        sweptUntil = nowUnixSeconds;
        foreach (var session in sessionManager.GetAll())
        {
            if (session.IsBot)
                continue;

            // The window has moved on, so a player left out now would keep an expired item's stats.
            try
            {
                await SweepAsync(session, since, nowUnixSeconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not sweep the expired items of {Name}", session.Name);
            }
        }
    }

    private async Task SweepAsync(UserSession session, long since, long nowUnixSeconds)
    {
        if (VipVault.ExpiredUnannounced(session.VipVaultExpiry, session.VipVaultExpiryAnnounced, nowUnixSeconds))
        {
            session.VipVaultExpiryAnnounced = true;
            await session.Client.SendPacket(WarehousePacketWriter.VipKeyExpired(VipVault.OwnStorage));
        }

        // Under the player's lock, as handlers check an item and then take it.
        var cleared = session.WithLock(s =>
        {
            ItemExpiry.Sweep(s.Warehouse, nowUnixSeconds);
            ItemExpiry.Sweep(s.VipWarehouse, nowUnixSeconds);
            return ItemExpiry.Sweep(s.Inventory, nowUnixSeconds, InventoryConstants.InventoryStart, InventoryConstants.HaveMax);
        });

        var ranOut = session.WithLock(s => ItemExpiry.RanOutWorn(s.Inventory, since, nowUnixSeconds));
        foreach (var itemId in ranOut)
            await equipmentEffects.ApplyRemovalEffectsAsync(session, itemId);
        if (ranOut.Count > 0)
            await userNotifications.SendStatUpdateAsync(session);

        if (cleared.Count == 0)
            return;

        var writer = new ItemCountChangePacketWriter();
        foreach (var slot in cleared)
            writer.Add((byte)slot, 0, 0, 0);
        await session.Client.SendPacket(writer.Build());
    }
}
