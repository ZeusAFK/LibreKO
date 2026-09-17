using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class UserDailyOpRepository(AppDbContext context) : IUserDailyOpRepository
{
    public async Task<UserDailyOp> GetOrCreateByCharacterId(int characterId)
    {
        var dailyOp = await context.UserDailyOps
            .SingleOrDefaultAsync(entry => entry.CharacterId == characterId);
        if (dailyOp != null)
            return dailyOp;

        dailyOp = new UserDailyOp { CharacterId = characterId };
        context.UserDailyOps.Add(dailyOp);
        await context.SaveChangesAsync();
        return dailyOp;
    }

    public async Task UpdateAsync(UserDailyOp dailyOp)
    {
        context.UserDailyOps.Update(dailyOp);
        await context.SaveChangesAsync();
    }
}
