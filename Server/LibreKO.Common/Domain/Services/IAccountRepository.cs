using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IAccountRepository
{
    Task<Account?> GetById(int id);
    Task<Account?> GetByLogin(string login);
    Task CreateAsync(Account account);
    Task UpdateAsync(Account account);
    Task UpdateWithCharactersAsync(Account account, IReadOnlyCollection<Character> characters);
    Task SetOnlineServerAsync(int accountId, int serverId);
    Task ClearOnlineServerAsync(int accountId);
    Task<int> ClearOnlineServerForServerAsync(int serverId);
}
