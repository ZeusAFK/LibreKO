using System;
using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class World
{
    private static readonly int HelmetGearIndex =
        System.Array.IndexOf(InventoryConstants.VisualSlots, InventoryConstants.Head);

    private void GraftEquipment(Node3D body, int race, int face, int[]? gear, int hair,
                               bool hideHelmet = false)
    {
        if (!_partsLoaded) { _partsIndex = LoadPartsIndex(); _partsLoaded = true; }
        if (_partsIndex == null) return;

        var parts = BodyParts(body);
        foreach (var part in parts.Values)
        {
            ItemShine.Clear(part);
            part.MaterialOverlay = null;
            part.MaterialOverride = null;
        }

        int grafted = 0, missing = 0;
        string? faceStem = FacePartStem(race, face);
        if (faceStem != null) { if (GraftPart(parts, 4, faceStem, "characters")) grafted++; else missing++; }

        bool helmet = !hideHelmet && gear != null
                      && gear.Length > HelmetGearIndex && gear[HelmetGearIndex] > 0;
        if (!helmet && HairPartStem(race, HairCode.StyleOf(hair)) is { } hairStem
            && GraftPart(parts, 5, hairStem, "characters"))
        {
            grafted++;
            if (HairCode.TintOf(hair) is { } tint) CharacterPreview.TintHair(parts[5], tint);
        }

        if (gear != null)
            foreach (var (gi, pn) in ArmorSlotToPart)
            {
                if (gi >= gear.Length || gear[gi] <= 0) continue;
                if (hideHelmet && gi == HelmetGearIndex) continue;
                string? stem = ArmorPartStem(gear[gi], race);
                if (stem == null)
                {
                    missing++; continue;
                }
                if (GraftPart(parts, pn, stem, "items/armor"))
                {
                    grafted++;
                    ItemShine.Apply(parts[pn], gear[gi], pn);
                }
                else missing++;
            }
        Cape.RefreshBody(body);
    }

    private bool GraftPart(System.Collections.Generic.Dictionary<int, MeshInstance3D> parts, int partNode, string stem, string dir)
    {
        if (!parts.TryGetValue(partNode, out var target)) return false;
        string resPath = $"res://assets/{dir}/{stem}.glb";

        if (!_partCache.TryGetValue(resPath, out var c))
        {
            Mesh? m = null; Skin? sk = null;
            if (ResourceLoader.Exists(resPath) && ResourceLoader.Load(resPath) is PackedScene scene)
            {
                var inst = scene.Instantiate<Node3D>();
                var src = FindFirst<MeshInstance3D>(inst);
                if (src?.Mesh != null)
                {
                    m = src.Mesh; sk = src.Skin;
                    target.Mesh = m; target.Skin = sk;
                    ForceDoubleSided(target);
                }
                inst.QueueFree();
            }
            c = (m, sk);
            _partCache[resPath] = c;
        }

        if (c.Mesh == null) return false;
        target.Mesh = c.Mesh;
        target.Skin = c.Skin;
        target.Visible = true;
        target.MaterialOverlay = null;
        return true;
    }

    private static int PartNodeIndex(string nodeName)
    {
        int i = nodeName.LastIndexOf("_part", System.StringComparison.Ordinal);
        if (i < 0) return -1;
        return int.TryParse(nodeName.AsSpan(i + 5), out int n) ? n : -1;
    }

    private string? ArmorPartStem(int itemId, int race)
    {
        if (_partsIndex == null) return null;
        if (!_partsIndex.Items.TryGetValue(itemId, out int r)
            && !_partsIndex.Items.TryGetValue(itemId / 1000 * 1000, out r))
            return null;
        int cat = r / 10000000, mid = (r / 1000) % 10000 + race, type = (r / 10) % 100, variant = r % 10;
        return $"{cat}_{mid:D4}_{type:D2}_{variant}";
    }

    private string? FacePartStem(int race, int face)
    {
        if (_partsIndex == null || !_partsIndex.Races.TryGetValue(race, out var rr) || rr.Face.Length == 0)
            return null;
        return $"{rr.Face}{face:D2}";
    }

    private string? HairPartStem(int race, int hair)
    {
        if (_partsIndex == null || !_partsIndex.Races.TryGetValue(race, out var rr) || rr.Hair.Length == 0)
            return null;
        return CharacterPreview.HairStem(rr.Hair, hair);
    }

    private static PartsIndex LoadPartsIndex()
    {
        var idx = new PartsIndex();
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

    private static System.Collections.Generic.Dictionary<int, string> LoadIdIndex(string path)
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
