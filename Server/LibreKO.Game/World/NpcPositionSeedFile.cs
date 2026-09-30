using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence.Seed;

namespace LibreKO.Game.World;

public static class NpcPositionSeedFile
{
    public static string ShardPath(string root, short zoneId) => SeedDataLocation.DataPath(root, $"NpcPositions.zone{zoneId:D3}.json");

    public static string MonolithPath(string root) => SeedDataLocation.DataPath(root, "NpcPositions.json");

    public static string? PathFor(string root, short zoneId)
    {
        var shard = ShardPath(root, zoneId);
        if (File.Exists(shard))
            return shard;
        var monolith = MonolithPath(root);
        return File.Exists(monolith) ? monolith : null;
    }

    public static bool TryUpdate(string path, NpcPosData before, NpcPosData after, out string error)
    {
        error = string.Empty;
        JsonArray rows;
        string newLine;
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            newLine = NewLineOf(text);
            rows = JsonNode.Parse(text)?.AsArray() ?? throw new JsonException();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            error = $"{Path.GetFileName(path)} could not be read: {e.Message}";
            return false;
        }

        var matches = rows.Where(row => row is JsonObject obj && Matches(obj, before)).ToList();
        if (matches.Count != 1)
        {
            error = matches.Count == 0
                ? $"{Path.GetFileName(path)} has no row like this one; the seed and the running server differ."
                : $"{Path.GetFileName(path)} has {matches.Count} identical rows; edit it by hand.";
            return false;
        }

        var target = matches[0]!.AsObject();
        target["LeftX"] = after.LeftX;
        target["TopZ"] = after.TopZ;
        target["NumNPC"] = after.NumNPC;
        target["RegTime"] = after.RegTime;
        target["Direction"] = after.Direction;
        target["SpawnRange"] = after.SpawnRange;
        File.WriteAllText(path, Render(rows, newLine), new UTF8Encoding(false));
        return true;
    }

    public static bool Matches(JsonObject row, NpcPosData pos) =>
        Int(row, "ZoneId") == pos.ZoneId
        && Int(row, "NpcId") == pos.NpcId
        && Int(row, "ActType") == pos.ActType
        && Int(row, "LeftX") == pos.LeftX
        && Int(row, "TopZ") == pos.TopZ
        && Int(row, "NumNPC") == pos.NumNPC
        && Int(row, "RegTime") == pos.RegTime
        && Int(row, "Direction") == pos.Direction
        && Int(row, "SpawnRange") == pos.SpawnRange
        && Int(row, "RegenType") == pos.RegenType
        && Int(row, "Room") == pos.Room;

    private static long Int(JsonObject row, string key) =>
        row.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<long>(out var number)
            ? number
            : long.MinValue;

    public static string NewLineOf(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    public static string Render(JsonArray rows, string newLine = "\r\n")
    {
        var text = new StringBuilder("[").Append(newLine);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i]!.AsObject();
            text.Append("  {").Append(newLine);
            var remaining = row.Count;
            foreach (var (key, value) in row)
            {
                remaining--;
                text.Append("    ").Append(Quote(key)).Append(": ").Append(Scalar(value));
                text.Append(remaining > 0 ? "," : "").Append(newLine);
            }
            text.Append(i < rows.Count - 1 ? "  }," : "  }").Append(newLine);
        }
        text.Append("]").Append(newLine);
        return text.ToString();
    }

    private static string Scalar(JsonNode? node) => node switch
    {
        null => "null",
        JsonValue value when value.GetValueKind() == JsonValueKind.String => Quote(value.GetValue<string>()),
        _ => node.ToJsonString(),
    };

    private static string Quote(string text)
    {
        var quoted = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': quoted.Append("\\\""); break;
                case '\\': quoted.Append("\\\\"); break;
                case '\n': quoted.Append("\\n"); break;
                case '\r': quoted.Append("\\r"); break;
                case '\t': quoted.Append("\\t"); break;
                case '\b': quoted.Append("\\b"); break;
                case '\f': quoted.Append("\\f"); break;
                default:
                    if (c < ' ')
                        quoted.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        quoted.Append(c);
                    break;
            }
        }
        return quoted.Append('"').ToString();
    }
}
