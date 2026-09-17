using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Common.Domain.Services;

public interface IKingRepository
{
    Task<List<KingSystemData>> GetKingsAsync();
}
