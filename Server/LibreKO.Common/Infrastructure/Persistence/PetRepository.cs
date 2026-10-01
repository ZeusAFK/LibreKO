using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class PetRepository(AppDbContext context) : IPetRepository
{
    public Task<Pet?> GetById(int id) =>
        context.Pets.AsNoTracking().SingleOrDefaultAsync(pet => pet.Id == id);

    public async Task<IReadOnlyList<Pet>> GetByIds(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0)
            return [];

        return await context.Pets.AsNoTracking().Where(pet => ids.Contains(pet.Id)).ToListAsync();
    }

    public Task<bool> NameExists(string name) =>
        context.Pets.AnyAsync(pet => pet.Name == name);

    public async Task<Pet> CreateAsync(Pet pet)
    {
        context.Pets.Add(pet);
        await context.SaveChangesAsync();
        context.Entry(pet).State = EntityState.Detached;
        return pet;
    }

    public async Task UpdateAsync(Pet pet)
    {
        context.Pets.Update(pet);
        await context.SaveChangesAsync();
        context.Entry(pet).State = EntityState.Detached;
    }
}
