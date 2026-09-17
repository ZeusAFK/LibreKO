using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IUserDailyOpRepository
{
    Task<UserDailyOp> GetOrCreateByCharacterId(int characterId);
    Task UpdateAsync(UserDailyOp dailyOp);
}
