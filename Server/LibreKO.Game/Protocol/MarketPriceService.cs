using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IMarketPriceService
{
    Task HandleAsync(IClient client, Packet packet);
    Task RecordAsync(UserSession buyer, UserSession seller, int itemId, int unitPrice, int quantity);
    Task PruneAsync(DateOnly today);
}

public class MarketPriceService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    ILogger<MarketPriceService> logger,
    TimeProvider? clock = null) : IMarketPriceService
{
    public const int RetentionDays = 30;
    private const int RequestLength = 4;
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < RequestLength)
            return;

        var itemId = packet.ReadInt();
        if (session.PremiumType == 0)
        {
            await client.SendPacket(MarketPricePacketWriter.Result(MarketPricePacketWriter.NotPremium));
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        if (now - session.MarketPriceAskedAt < MinimumInterval)
        {
            await client.SendPacket(MarketPricePacketWriter.Result(MarketPricePacketWriter.TooSoon));
            return;
        }
        session.MarketPriceAskedAt = now;

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.MarketPriceDays.AsNoTracking()
            .Where(d => d.ItemId == itemId && d.Trades > 0)
            .OrderByDescending(d => d.Day)
            .Take(MarketPriceSummary.DaysShown)
            .ToListAsync();

        var summary = MarketPriceSummary.Of(rows);
        await client.SendPacket(summary == null
            ? MarketPricePacketWriter.Result(MarketPricePacketWriter.NoHistory)
            : MarketPricePacketWriter.Days(itemId, summary));
    }

    public async Task RecordAsync(UserSession buyer, UserSession seller, int itemId, int unitPrice, int quantity)
    {
        if (buyer.IsBot || seller.IsBot || unitPrice <= 0 || quantity <= 0)
            return;

        var now = _clock.GetUtcNow().UtcDateTime;
        var day = DateOnly.FromDateTime(now);
        await _writeLock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.MarketPriceDays.FindAsync(itemId, day);
            if (row == null)
            {
                row = new MarketPriceDay { ItemId = itemId, Day = day, MinPrice = unitPrice, MaxPrice = unitPrice };
                db.MarketPriceDays.Add(row);
            }

            row.Trades++;
            row.Quantity += quantity;
            row.Total += (long)unitPrice * quantity;
            row.MinPrice = Math.Min(row.MinPrice, unitPrice);
            row.MaxPrice = Math.Max(row.MaxPrice, unitPrice);
            row.LastTradeAt = now;
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Market price of {ItemId} not recorded", itemId);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task PruneAsync(DateOnly today)
    {
        var cutoff = today.AddDays(-RetentionDays);
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var expired = await db.MarketPriceDays.Where(d => d.Day < cutoff).ToListAsync();
        if (expired.Count == 0)
            return;

        db.MarketPriceDays.RemoveRange(expired);
        await db.SaveChangesAsync();
        logger.LogInformation("Pruned {Count} market price days before {Cutoff}", expired.Count, cutoff);
    }
}
