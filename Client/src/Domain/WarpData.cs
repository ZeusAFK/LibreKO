using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class WarpData
{
    public readonly struct Destination
    {
        public readonly string Name;
        public readonly string Description;
        public readonly string Image;
        public readonly int MinLevel;
        public readonly int MaxLevel;

        public Destination(string name, string description, string image, int minLevel, int maxLevel)
        {
            Name = name; Description = description; Image = image;
            MinLevel = minLevel; MaxLevel = maxLevel;
        }
    }

    private static readonly Dictionary<int, Destination> _destinations = new();
    private static readonly Dictionary<string, Texture2D?> _images = new();
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        using var f = Godot.FileAccess.Open("res://assets/ui/warp/warps.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushWarning("[warp] missing warps.json (run tools/bake_warps.py)"); return; }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;

        foreach (var pair in parsed.AsGodotDictionary())
        {
            if (!int.TryParse(pair.Key.AsString(), out int id)
                || pair.Value.VariantType != Variant.Type.Dictionary) continue;
            var d = pair.Value.AsGodotDictionary();
            _destinations[id] = new Destination(
                d.TryGetValue("n", out var n) ? n.AsString() : "",
                d.TryGetValue("d", out var desc) ? desc.AsString() : "",
                d.TryGetValue("img", out var img) ? img.AsString() : "",
                d.TryGetValue("lo", out var lo) ? lo.AsInt32() : 0,
                d.TryGetValue("hi", out var hi) ? hi.AsInt32() : 0);
        }
    }

    public static bool TryGet(int warpId, out Destination destination)
    {
        EnsureLoaded();
        return _destinations.TryGetValue(warpId, out destination);
    }

    public static Texture2D? Image(string stem)
    {
        if (stem.Length == 0) return null;
        if (_images.TryGetValue(stem, out var cached)) return cached;
        var tex = ResourceLoader.Load<Texture2D>($"res://assets/ui/warp/{stem}.png");
        _images[stem] = tex;
        return tex;
    }
}
