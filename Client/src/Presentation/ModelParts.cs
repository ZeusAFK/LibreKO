using Godot;

namespace LibreKO.Presentation;

internal static class ModelParts
{
    internal static readonly int HelmetGearIndex =
        System.Array.IndexOf(InventoryConstants.VisualSlots, InventoryConstants.Head);

    internal static int PartNodeIndex(string nodeName)
    {
        int i = nodeName.LastIndexOf("_part", System.StringComparison.Ordinal);
        if (i < 0) return -1;
        return int.TryParse(nodeName.AsSpan(i + 5), out int n) ? PlayerRig.BodyPartSlot(nodeName[..i], n) : -1;
    }

    internal static ModelNodes.PartsIndex LoadPartsIndex()
    {
        var idx = new ModelNodes.PartsIndex();
        using (var f = Godot.FileAccess.Open("res://assets/items/armor/index.json", Godot.FileAccess.ModeFlags.Read))
            if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } root
                && root.TryGetValue("items", out var itemsV) && itemsV.AsGodotDictionary() is { } items)
                foreach (var k in items.Keys)
                    if (int.TryParse(k.AsString(), out int id)) idx.Items[id] = items[k].AsInt32();
        using (var f = Godot.FileAccess.Open("res://assets/characters/faces.json", Godot.FileAccess.ModeFlags.Read))
            if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } root
                && root.TryGetValue("races", out var racesV) && racesV.AsGodotDictionary() is { } races)
                foreach (var k in races.Keys)
                    if (int.TryParse(k.AsString(), out int rid) && races[k].AsGodotDictionary() is { } e)
                        idx.Races[rid] = (e.TryGetValue("facePart", out var fp) ? fp.AsString() : "",
                                          e.TryGetValue("hairPart", out var hp) ? hp.AsString() : "");
        return idx;
    }

    internal static System.Collections.Generic.Dictionary<int, string> LoadIdIndex(string path)
    {
        var map = new System.Collections.Generic.Dictionary<int, string>();
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return map;
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType == Variant.Type.Dictionary)
        {
            var d = parsed.AsGodotDictionary();
            foreach (var k in d.Keys)
                if (int.TryParse(k.AsString(), out int id))
                    map[id] = d[k].AsString();
        }
        return map;
    }
}
