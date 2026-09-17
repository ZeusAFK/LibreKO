using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class WarehouseRepository(AppDbContext context) : IWarehouseRepository
{
    public async Task<Warehouse> GetOrCreateByAccountId(int accountId)
    {
        var warehouse = await context.Warehouses.SingleOrDefaultAsync(entry => entry.AccountId == accountId);
        if (warehouse != null)
            return warehouse;

        warehouse = new Warehouse
        {
            AccountId = accountId
        };

        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        return warehouse;
    }

    public async Task UpdateAsync(Warehouse warehouse)
    {
        context.Warehouses.Update(warehouse);
        await context.SaveChangesAsync();
    }
}
