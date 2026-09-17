namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public interface IDataSeeder
{
    Task<bool> SeedEntityAsync<T>(IEntitySeed<T> entitySeed, bool? saveChanges = true, bool force = false) where T : class;
}
