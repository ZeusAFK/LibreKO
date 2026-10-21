using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class KnightsRepository(AppDbContext context) : IKnightsRepository
{
    public async Task<bool> IsNameTakenAsync(string name)
    {
        return await context.Set<KnightsEntity>().AnyAsync(c => c.Name == name);
    }

    public async Task CreateAsync(KnightsEntity clan)
    {
        context.Set<KnightsEntity>().Add(clan);
        await context.SaveChangesAsync();
    }

    public async Task<KnightsEntity?> FindAsync(short id)
    {
        return await context.Set<KnightsEntity>().FindAsync(id);
    }

    public async Task RemoveAsync(KnightsEntity entity)
    {
        context.Set<KnightsEntity>().Remove(entity);
        await context.SaveChangesAsync();
    }

    // A save of a clan held in memory never writes its name.
    public async Task UpdateAsync(KnightsEntity clan)
    {
        context.Set<KnightsEntity>().Update(clan).Property(entity => entity.Name).IsModified = false;
        await context.SaveChangesAsync();
    }

    public async Task<List<ClanMemberProjection>> GetMembersAsync(short knightsId)
    {
        return await context.Characters
            .Where(c => c.KnightsId == knightsId)
            .Select(c => new ClanMemberProjection
            {
                Name = c.Name,
                Fame = c.Fame,
                Level = c.Level,
                Class = c.Class,
                Loyalty = c.Loyalty,
                DonatedPoints = c.KnightsPoints,
                LastOnlineTime = c.LastOnlineTime,
                Memo = c.ClanMemo
            })
            .ToListAsync();
    }

    public async Task<List<Character>> GetCharactersByClanAsync(short knightsId)
    {
        return await context.Characters
            .Where(c => c.KnightsId == knightsId)
            .ToListAsync();
    }

    public async Task<List<ClanMemberLoyalty>> GetMemberLoyaltyAsync()
    {
        return await context.Characters
            .Where(c => c.KnightsId > 0)
            .Select(c => new ClanMemberLoyalty(c.KnightsId, c.Name, c.Loyalty))
            .ToListAsync();
    }

    public async Task SyncCharacterClanStateAsync(int characterId, short knightsId, byte fame, int? money = null, int? loyalty = null)
    {
        var character = await context.Characters.FindAsync(characterId);
        if (character == null)
            return;

        if (character.KnightsId != knightsId)
            character.ClanMemo = "";
        character.KnightsId = knightsId;
        character.Fame = fame;

        if (money.HasValue)
            character.Money = money.Value;

        if (loyalty.HasValue)
            character.Loyalty = loyalty.Value;

        await context.SaveChangesAsync();
    }

    public async Task SetClanMemoAsync(int characterId, string memo)
    {
        var character = await context.Characters.FindAsync(characterId);
        if (character == null)
            return;

        character.ClanMemo = memo;
        await context.SaveChangesAsync();
    }
}
