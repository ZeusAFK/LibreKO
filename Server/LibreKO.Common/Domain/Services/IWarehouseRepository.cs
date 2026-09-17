using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IWarehouseRepository
{
    Task<Warehouse> GetOrCreateByAccountId(int accountId);
    Task UpdateAsync(Warehouse warehouse);
}
