using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;

namespace LibreKO.Common.Domain.Services;

public readonly record struct CharacterRankRow(
    int Id,
    string Name,
    AccountNation Nation,
    short KnightsId,
    int Loyalty,
    int LoyaltyMonthly,
    int LoyaltyDaily);

public interface ICharacterRepository
{
    Task<Character?> GetById(int id);
    Task<IEnumerable<Character>> GetCharactersByAccount(int accountId);
    Task CreateAsync(Character character);
    Task UpdateAsync(Character character);

    Task<int> UpdateQuestDataAsync(int characterId, byte[] questData);

    Task<bool> IsNameTaken(string name);
    Task<Character?> GetByName(string name);

    Task<IReadOnlyList<CharacterRankRow>> GetTopByLoyalty(AccountNation nation, int count);

    Task<int> GetLoyaltyRank(AccountNation nation, int loyalty);

    Task<int> ResetDailyLoyaltyAll();
}
