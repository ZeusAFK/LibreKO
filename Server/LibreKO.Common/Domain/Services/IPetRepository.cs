using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IPetRepository
{
    Task<Pet?> GetById(int id);
    Task<IReadOnlyList<Pet>> GetByIds(IReadOnlyCollection<int> ids);
    Task<bool> NameExists(string name);
    Task<Pet> CreateAsync(Pet pet);
    Task UpdateAsync(Pet pet);
}
