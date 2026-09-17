using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IKnightsAllianceRepository
{
    Task<List<KnightsAllianceEntity>> GetAllAsync();
    Task<KnightsAllianceEntity?> FindByMainClanAsync(short mainClanId);
    Task CreateAsync(KnightsAllianceEntity alliance);
    Task UpdateAsync(KnightsAllianceEntity alliance);
    Task RemoveAsync(short mainClanId);
}
