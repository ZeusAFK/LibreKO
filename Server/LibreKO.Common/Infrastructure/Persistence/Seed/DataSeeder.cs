using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public class DataSeeder(AppDbContext db, ILogger<DataSeeder> logger) : IDataSeeder
{
    public async Task<bool> SeedEntityAsync<T>(IEntitySeed<T> entitySeed, bool? saveChanges = true, bool force = false) where T : class
    {
        var dbSet = db.Set<T>();
        var seedName = entitySeed.GetType().Name;
        var fileBackedSeed = entitySeed as IFileBackedSeed;

        if (fileBackedSeed is { SeedSources.Count: 0 })
        {
            logger.LogWarning(
                "Skipping seed {SeedName}: seed file not found at {SeedPath}",
                seedName,
                fileBackedSeed.SeedPath);
            return false;
        }

        var trackFingerprint = fileBackedSeed != null && saveChanges is true;
        var fingerprint = trackFingerprint
            ? await SeedFingerprint.ComputeAsync(entitySeed, fileBackedSeed!.SeedSources)
            : null;

        if (fingerprint != null && !force && await IsUpToDateAsync(dbSet, seedName, fingerprint))
            return false;

        var entityType = db.Model.FindEntityType(typeof(T));

        if (entityType == null)
        {
            logger.LogError("Unable to seed data for entity type {EntityType}", typeof(T));
            return false;
        }

        var primaryKey = entityType.FindPrimaryKey();

        if (primaryKey == null)
        {
            logger.LogError("Unable to find primary key for entity type {EntityType}", typeof(T));
            return false;
        }

        var existingData = await dbSet.IgnoreQueryFilters().AsNoTracking().ToListAsync();

        var keyProperties = primaryKey.Properties;
        var existingDataDict = BuildKeyDictionary(existingData, keyProperties, "database");
        var seedDataDict = BuildKeyDictionary(entitySeed.GetSeedData(), keyProperties, "seed");

        var entitiesToAdd = entitySeed.PerformInsert
            ? seedDataDict
                .Where(sd => !existingDataDict.ContainsKey(sd.Key))
                .Select(sd => sd.Value)
                .ToList()
            : [];

        var entitiesToDelete = entitySeed.PerformDelete
            ? existingDataDict
                .Where(ed => !seedDataDict.ContainsKey(ed.Key))
                .Select(ed => ed.Value)
                .ToList()
            : [];

        var entitiesToUpdate = entitySeed.PerformUpdate
            ? existingDataDict
                .Where(ed => seedDataDict.ContainsKey(ed.Key))
                .Where(ed => !AreEntitiesEqual(keyProperties, ed.Value, seedDataDict[ed.Key], entitySeed.PropertiesToExclude, entitySeed.PropertiesToUpdate))
                .Select(sd => sd.Value)
                .ToList()
            : [];

        if (entitiesToAdd.Count != 0)
        {
            await dbSet.AddRangeAsync(entitiesToAdd);
        }

        if (entitiesToUpdate.Count != 0)
        {
            foreach (var entity in entitiesToUpdate)
            {
                var key = GetKeyString(keyProperties, entity);
                var updatedEntity = seedDataDict.GetValueOrDefault(key);

                if (updatedEntity != null)
                {
                    UpdateEntity(keyProperties, entity, updatedEntity, entitySeed.PropertiesToExclude, entitySeed.PropertiesToUpdate);
                }
            }

            dbSet.UpdateRange(entitiesToUpdate);
        }

        if (entitiesToDelete.Count != 0)
        {
            dbSet.RemoveRange(entitiesToDelete);
        }

        if (entitiesToAdd.Count != 0 || entitiesToUpdate.Count != 0 || entitiesToDelete.Count != 0)
        {
            logger.LogInformation("Seeding {EntityType}: +{Added} ~{Updated} -{Deleted}",
                typeof(T).Name, entitiesToAdd.Count, entitiesToUpdate.Count, entitiesToDelete.Count);

            try
            {
                if (saveChanges is true)
                    await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to seed entity {EntityName}", typeof(T));
                throw;
            }
        }

        if (fingerprint != null)
        {
            await RecordStateAsync(
                seedName,
                fingerprint,
                existingDataDict.Count + entitiesToAdd.Count - entitiesToDelete.Count);
        }

        return true;
    }

    private async Task<bool> IsUpToDateAsync<T>(DbSet<T> dbSet, string seedName, string fingerprint) where T : class
    {
        var recorded = await db.Set<SeedState>().AsNoTracking()
            .FirstOrDefaultAsync(state => state.Name == seedName);

        if (recorded == null || recorded.Fingerprint != fingerprint)
            return false;

        var rowCount = await dbSet.IgnoreQueryFilters().LongCountAsync();
        if (rowCount == recorded.RowCount)
            return true;

        logger.LogInformation(
            "Reseeding {SeedName}: {EntityType} holds {RowCount} rows, expected {ExpectedRowCount}",
            seedName, typeof(T).Name, rowCount, recorded.RowCount);
        return false;
    }

    private async Task RecordStateAsync(string seedName, string fingerprint, long rowCount)
    {
        var state = await db.Set<SeedState>().FirstOrDefaultAsync(s => s.Name == seedName);

        if (state == null)
        {
            state = new SeedState { Name = seedName };
            db.Set<SeedState>().Add(state);
        }

        state.Fingerprint = fingerprint;
        state.RowCount = rowCount;
        state.AppliedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    private static void UpdateEntity<T>(IEnumerable<IProperty> keyProperties, T entity1, T entity2, string[] propertiesToExclude, string[] propertiesToUpdate)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite
                && !keyProperties.Any(kp => kp.Name == p.Name)
                && !IsNavigationProperty(p)
                && !propertiesToExclude.Contains(p.Name));

        if (propertiesToUpdate.Length > 0)
        {
            properties = properties.Where(p => propertiesToUpdate.Contains(p.Name));
        }

        foreach (var prop in properties)
        {
            var value2 = prop.GetValue(entity2);
            if (!Equals(prop.GetValue(entity1), value2))
            {
                prop.SetValue(entity1, value2);
            }
        }
    }

    private static string GetKeyString<T>(IEnumerable<IProperty> keyProperties, T entity)
    {
        var keyValues = keyProperties.Select(p => typeof(T).GetProperty(p.Name)?.GetValue(entity));
        return string.Join(";", keyValues);
    }

    private static Dictionary<string, T> BuildKeyDictionary<T>(IEnumerable<T> entities, IEnumerable<IProperty> keyProperties, string sourceName)
    {
        var dict = new Dictionary<string, T>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            var key = GetKeyString(keyProperties, entity);
            if (!dict.TryAdd(key, entity))
            {
                throw new InvalidOperationException(
                    $"Duplicate primary key '{key}' found in {sourceName} data for entity '{typeof(T).Name}'. " +
                    "Review the entity key mapping or the source seed data.");
            }
        }

        return dict;
    }

    private static bool AreEntitiesEqual<T>(IEnumerable<IProperty> keyProperties, T entity1, T entity2, string[] propertiesToExclude, string[]? propertiesToUpdate = null)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite
                && !keyProperties.Any(kp => kp.Name == p.Name)
                && !IsNavigationProperty(p)
                && !propertiesToExclude.Contains(p.Name));

        if (propertiesToUpdate is { Length: > 0 })
        {
            properties = properties.Where(p => propertiesToUpdate.Contains(p.Name));
        }

        foreach (var prop in properties)
        {
            var value1 = prop.GetValue(entity1);
            var value2 = prop.GetValue(entity2);

            if (!Equals(value1, value2))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsNavigationProperty(PropertyInfo property)
    {
        return typeof(IEnumerable<object>).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string);
    }
}
