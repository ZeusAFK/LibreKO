namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public interface IEntitySeed<T>
{
    Type EntityType { get; }
    IEnumerable<T> GetSeedData();
    string[] PropertiesToExclude { get; }
    string[] PropertiesToUpdate { get; }
    bool PerformInsert { get; }
    bool PerformUpdate { get; }
    bool PerformDelete { get; }
}

public interface IFileBackedSeed
{
    string SeedPath { get; }
    IReadOnlyList<string> SeedSources { get; }
}
