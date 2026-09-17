using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Common.Infrastructure.Persistence;

public class PatchRepository(IServiceProvider serviceProvider, IMemoryCache cache) : IPatchRepository
{
    private const string PatchListCacheKey = "patches:all";

    public async Task<List<Patch>> GetPatchList()
    {
        return await cache.GetOrCreateAsync(PatchListCacheKey, async entry =>
        {
            entry.Priority = CacheItemPriority.NeverRemove;
            
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return await context.Patches.AsNoTracking().ToListAsync();
        }) ?? [];
    }
}
