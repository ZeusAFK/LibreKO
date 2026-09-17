using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Common.Infrastructure.Persistence;

public class KingRepository(IServiceProvider serviceProvider, IMemoryCache cache) : IKingRepository
{
    private const string KingsCacheKey = "kings:all";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<List<KingSystemData>> GetKingsAsync()
    {
        return await cache.GetOrCreateAsync(KingsCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;

            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await context.KingSystem.AsNoTracking().ToListAsync();
        }) ?? [];
    }
}
