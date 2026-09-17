using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class CharacterRepository(AppDbContext context) : ICharacterRepository
{
    public async Task<Character?> GetById(int id)
    {
        return await context.Characters.FindAsync(id);
    }

    public async Task<IEnumerable<Character>> GetCharactersByAccount(int accountId)
    {
        return await context.Characters
            .Where(c => c.AccountId == accountId)
            .OrderBy(c => c.Slot)
            .ToListAsync();
    }

    public async Task CreateAsync(Character character)
    {
        context.Characters.Add(character);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Character character)
    {
        context.Characters.Update(character);
        await context.SaveChangesAsync();
    }

    public async Task<int> UpdateQuestDataAsync(int characterId, byte[] questData)
    {
        return await context.Characters
            .Where(c => c.Id == characterId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.QuestData, questData));
    }

    public async Task<bool> IsNameTaken(string name)
    {
        return await context.Characters.AnyAsync(c => c.Name == name);
    }

    public async Task<Character?> GetByName(string name)
    {
        return await context.Characters.SingleOrDefaultAsync(c => c.Name == name);
    }

    public async Task<IReadOnlyList<CharacterRankRow>> GetTopByLoyalty(AccountNation nation, int count)
    {
        if (count <= 0) return Array.Empty<CharacterRankRow>();

        var rows = await (from ch in context.Characters
                          join acc in context.Accounts on ch.AccountId equals acc.Id
                          where acc.Nation == nation && ch.LoyaltyDaily > 0 && ch.DeletionTime == null
                          orderby ch.LoyaltyDaily descending
                          select new CharacterRankRow(
                              ch.Id,
                              ch.Name,
                              acc.Nation,
                              ch.KnightsId,
                              ch.Loyalty,
                              ch.LoyaltyMonthly,
                              ch.LoyaltyDaily))
                         .Take(count)
                         .ToListAsync();
        return rows;
    }

    public async Task<int> GetLoyaltyRank(AccountNation nation, int dailyLoyalty)
    {
        if (dailyLoyalty <= 0) return 0;

        var higher = await (from ch in context.Characters
                            join acc in context.Accounts on ch.AccountId equals acc.Id
                            where acc.Nation == nation && ch.LoyaltyDaily > dailyLoyalty && ch.DeletionTime == null
                            select ch.Id).CountAsync();
        return higher + 1;
    }

    public async Task<int> ResetDailyLoyaltyAll()
    {
        return await context.Characters
            .Where(c => c.LoyaltyDaily != 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.LoyaltyDaily, 0));
    }
}
