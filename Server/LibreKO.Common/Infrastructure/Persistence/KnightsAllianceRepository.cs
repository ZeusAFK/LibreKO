using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class KnightsAllianceRepository(AppDbContext context) : IKnightsAllianceRepository
{
    public async Task<List<KnightsAllianceEntity>> GetAllAsync()
    {
        return await context.KnightsAlliances.AsNoTracking().ToListAsync();
    }

    public async Task<KnightsAllianceEntity?> FindByMainClanAsync(short mainClanId)
    {
        return await context.KnightsAlliances.FindAsync(mainClanId);
    }

    public async Task CreateAsync(KnightsAllianceEntity alliance)
    {
        context.KnightsAlliances.Add(alliance);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(KnightsAllianceEntity alliance)
    {
        context.KnightsAlliances.Update(alliance);
        await context.SaveChangesAsync();
    }

    public async Task RemoveAsync(short mainClanId)
    {
        var existing = await context.KnightsAlliances.FindAsync(mainClanId);
        if (existing == null) return;
        context.KnightsAlliances.Remove(existing);
        await context.SaveChangesAsync();
    }
}
