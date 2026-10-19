using Godot;

namespace LibreKO.Content;

internal static class JsonTables
{
    internal static Godot.Collections.Dictionary? ReadObject(string path, string? missingWarning = null)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null)
        {
            if (missingWarning != null) GD.PushWarning(missingWarning);
            return null;
        }
        var parsed = Json.ParseString(f.GetAsText());
        return parsed.VariantType == Variant.Type.Dictionary ? parsed.AsGodotDictionary() : null;
    }

    internal static void ReadIdStrings(Godot.Collections.Dictionary root, string key, Action<int, string> add)
    {
        if (!root.TryGetValue(key, out var v) || v.VariantType != Variant.Type.Dictionary) return;
        var map = v.AsGodotDictionary();
        foreach (var k in map.Keys)
            if (int.TryParse(k.AsString(), out int id))
                add(id, map[k].AsString());
    }
}
