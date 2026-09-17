using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Common.Infrastructure.Persistence;

public class ServerRepository(IServiceProvider serviceProvider, IMemoryCache cache) : IServerRepository
{
    private const string ServersCacheKey = "servers:all";

    public async Task<List<Server>> GetServers()
    {
        return await cache.GetOrCreateAsync(ServersCacheKey, async entry =>
        {
            // Short TTL, NOT NeverRemove: the server row's OnlinePlayers is updated
            // out-of-process by the game server every 120s, and other fields (MaxPlayers,
            // IP) change in the DB too. A permanent cache froze a stale snapshot (e.g.
            // OnlinePlayers > MaxPlayers → user-count -1 → client shows "server closed").
            // 10s still shields the DB during a login storm while staying fresh.
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);

            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await context.Servers.AsNoTracking().Include(server => server.Group).ToListAsync();
        }) ?? [];
    }

    public async Task UpdateOnlinePlayersAsync(int serverId, int onlinePlayers)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var server = await context.Servers.FindAsync(serverId);
        if (server == null) return;
        server.OnlinePlayers = onlinePlayers;
        await context.SaveChangesAsync();
    }
}
