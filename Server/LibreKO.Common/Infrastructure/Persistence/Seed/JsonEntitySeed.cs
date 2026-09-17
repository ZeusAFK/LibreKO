using System.Text.Json;
using System.Reflection;

namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public abstract class JsonEntitySeed<T> : IEntitySeed<T>, IFileBackedSeed
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        options.Converters.Add(new FlexibleBooleanJsonConverter());
        options.Converters.Add(new FlexibleByteJsonConverter());
        options.Converters.Add(new FlexibleSByteJsonConverter());
        options.Converters.Add(new FlexibleInt16JsonConverter());
        options.Converters.Add(new FlexibleUInt16JsonConverter());
        options.Converters.Add(new FlexibleInt32JsonConverter());
        options.Converters.Add(new FlexibleInt64JsonConverter());
        options.Converters.Add(new FlexibleDoubleJsonConverter());
        return options;
    }

    public Type EntityType => typeof(T);
    public virtual bool PerformInsert => true;
    public virtual bool PerformUpdate => true;
    public virtual bool PerformDelete => false;
    public virtual string[] PropertiesToExclude => [];
    public virtual string[] PropertiesToUpdate => [];
    public string SeedPath => SeedDataLocation.DataPath(JsonFileName);
    public IReadOnlyList<string> SeedSources => File.Exists(SeedPath) ? [SeedPath] : ShardPaths();

    protected abstract string JsonFileName { get; }

    // A seed too large for one file lists its shards here, e.g. "Items.slot*.json".
    protected virtual string? ShardPattern => null;

    private string[] ShardPaths()
    {
        if (ShardPattern is null)
            return [];

        var directory = Path.GetDirectoryName(SeedPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return [];

        var paths = Directory.GetFiles(directory, ShardPattern);
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    public virtual IEnumerable<T> GetSeedData()
    {
        var sources = SeedSources;
        if (sources.Count == 0)
            return [];

        var entities = new List<T>();
        foreach (var path in sources)
        {
            var json = File.ReadAllText(path);
            var part = JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
            if (part is not null)
                entities.AddRange(part);
        }

        NormalizeStringProperties(entities);
        return entities;
    }

    private static void NormalizeStringProperties(IEnumerable<T> entities)
    {
        var stringProperties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string) && property.CanRead && property.CanWrite)
            .ToArray();

        if (stringProperties.Length == 0)
            return;

        foreach (var entity in entities)
        {
            foreach (var property in stringProperties)
            {
                if (property.GetValue(entity) == null)
                    property.SetValue(entity, string.Empty);
            }
        }
    }
}
