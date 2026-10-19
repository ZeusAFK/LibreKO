using Godot;

namespace LibreKO.Content;

internal static class WarpDataLoader
{
    internal static void Load()
    {
        using var f = Godot.FileAccess.Open("res://assets/ui/warp/warps.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[warp] missing warps.json (run tools/bake_warps.py)"); return; }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;

        foreach (var pair in parsed.AsGodotDictionary())
        {
            if (!int.TryParse(pair.Key.AsString(), out int id)
                || pair.Value.VariantType != Variant.Type.Dictionary) continue;
            var d = pair.Value.AsGodotDictionary();
            WarpData.Add(id, new WarpData.Destination(
                d.TryGetValue("n", out var n) ? n.AsString() : "",
                d.TryGetValue("d", out var desc) ? desc.AsString() : "",
                d.TryGetValue("img", out var img) ? img.AsString() : "",
                d.TryGetValue("lo", out var lo) ? lo.AsInt32() : 0,
                d.TryGetValue("hi", out var hi) ? hi.AsInt32() : 0));
        }
    }
}
