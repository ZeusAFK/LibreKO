using System.Collections.Generic;
using System.Text.Json;

namespace LibreKO.Domain;

public readonly record struct ItemInventoryDefinition(int Weight, int Countable);

public static class ItemInventoryCatalog
{
    public const int SchemaVersion = 1;
    private const int InventoryFields = 2;

    public static Dictionary<int, ItemInventoryDefinition> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var version)
            || version.ValueKind != JsonValueKind.Number
            || !version.TryGetInt32(out int schemaVersion) || schemaVersion != SchemaVersion
            || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
            throw new JsonException("Unsupported item inventory catalog.");

        var definitions = new Dictionary<int, ItemInventoryDefinition>();
        foreach (var row in items.EnumerateObject())
        {
            var values = row.Value;
            if (!int.TryParse(row.Name, out int id) || id <= 0
                || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != InventoryFields
                || values[0].ValueKind != JsonValueKind.Number || values[1].ValueKind != JsonValueKind.Number
                || !values[0].TryGetInt32(out int weight) || !values[1].TryGetInt32(out int countable)
                || weight < 0 || weight > short.MaxValue || countable < 0 || countable > byte.MaxValue
                || !definitions.TryAdd(id, new ItemInventoryDefinition(weight, countable)))
                throw new JsonException("Invalid item inventory catalog entry.");
        }
        return definitions;
    }
}
