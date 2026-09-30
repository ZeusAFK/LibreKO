using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LibreKO.Game.World;

public interface INpcSpawnRowStore
{
    Task<bool> UpdateAsync(NpcPosData row);
}

public sealed class NpcSpawnRowStore(IServiceScopeFactory scopeFactory) : INpcSpawnRowStore
{
    public async Task<bool> UpdateAsync(NpcPosData row)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.NpcPositions.FindAsync(row.Index);
        if (stored == null)
            return false;

        stored.LeftX = row.LeftX;
        stored.TopZ = row.TopZ;
        stored.NumNPC = row.NumNPC;
        stored.RegTime = row.RegTime;
        stored.Direction = row.Direction;
        stored.SpawnRange = row.SpawnRange;
        await db.SaveChangesAsync();
        return true;
    }
}
