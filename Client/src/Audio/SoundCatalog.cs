using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static class SoundCatalog
{
    public enum Kind { TwoD = 0, ThreeD = 1, Stream = 2 }

    public readonly record struct Entry(string File, Kind Type, int Instances, float GainDb = 0f);

    public readonly record struct LooksSounds(
        int Move, int Attack0, int Attack1, int Struck0, int Struck1,
        int Dead0, int Dead1, int Breathe0, int Breathe1);

    public readonly record struct ZoneBgm(int ElBattle, int ElAmbient, int KaBattle, int KaAmbient);

    public readonly record struct Footsteps(int Walk1, int Walk2, int Run1, int Run2, int Run3);

    public const string Dir = "res://assets/sounds/";

    private static readonly Dictionary<int, Entry> sounds = new();
    private static readonly Dictionary<int, LooksSounds> looks = new();
    private static readonly Dictionary<int, (int, int)> items = new();
    private static readonly Dictionary<int, int> fxById = new();
    private static readonly Dictionary<string, int> fxByName = new();
    private static readonly Dictionary<int, ZoneBgm> zones = new();
    private static readonly Dictionary<int, Footsteps> steps = new();
    private static bool loaded;

    public static int Count => sounds.Count;

    public static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;

        using var f = Godot.FileAccess.Open(Dir + "sounds.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null)
        {
            GD.PushWarning("[sound] missing sounds.json (run tools/bake_sounds.py) — audio disabled");
            return;
        }
        var parsed = Json.ParseString(f.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var root = parsed.AsGodotDictionary();

        foreach (var (key, val) in Each(root, "sounds"))
        {
            if (!int.TryParse(key, out int id)) continue;
            var o = val.AsGodotDictionary();
            float gain = o.TryGetValue("gain", out var g) ? (float)g.AsDouble() : 0f;
            sounds[id] = new Entry(Str(o, "file"), (Kind)Int(o, "type"), Mathf.Max(1, Int(o, "inst")), gain);
        }

        foreach (var (key, val) in Each(root, "looks"))
        {
            if (!int.TryParse(key, out int id)) continue;
            var o = val.AsGodotDictionary();
            looks[id] = new LooksSounds(
                Int(o, "move"), Int(o, "attack0"), Int(o, "attack1"),
                Int(o, "struck0"), Int(o, "struck1"), Int(o, "dead0"), Int(o, "dead1"),
                Int(o, "breathe0"), Int(o, "breathe1"));
        }

        foreach (var (key, val) in Each(root, "items"))
        {
            if (!int.TryParse(key, out int id)) continue;
            var a = val.AsGodotArray();
            items[id] = (a.Count > 0 ? a[0].AsInt32() : 0, a.Count > 1 ? a[1].AsInt32() : 0);
        }

        foreach (var (key, val) in Each(root, "fx"))
            if (int.TryParse(key, out int fxId)) fxById[fxId] = val.AsInt32();

        foreach (var (key, val) in Each(root, "fxByName"))
            fxByName[key.ToLowerInvariant()] = val.AsInt32();

        foreach (var (key, val) in Each(root, "zones"))
        {
            if (!int.TryParse(key, out int id)) continue;
            var o = val.AsGodotDictionary();
            zones[id] = new ZoneBgm(Int(o, "elBattle"), Int(o, "elAmbient"),
                                     Int(o, "kaBattle"), Int(o, "kaAmbient"));
        }

        foreach (var (key, val) in Each(root, "footsteps"))
        {
            if (!int.TryParse(key, out int id)) continue;
            var o = val.AsGodotDictionary();
            steps[id] = new Footsteps(Int(o, "walk1"), Int(o, "walk2"),
                                       Int(o, "run1"), Int(o, "run2"), Int(o, "run3"));
        }

        GD.Print($"[sound] {sounds.Count} sounds · {looks.Count} looks · {items.Count} items · " +
                 $"{fxByName.Count} fx · {zones.Count} zones");
    }

    public static bool TryGet(int id, out Entry e)
    {
        EnsureLoaded();
        return sounds.TryGetValue(id, out e);
    }

    public static LooksSounds? Looks(int looksId)
    {
        EnsureLoaded();
        return looks.TryGetValue(looksId, out var l) ? l : null;
    }

    public static int ItemSwing(int itemId) => ItemPair(itemId).Item1;

    public static int ItemImpact(int itemId) => ItemPair(itemId).Item2;

    private static (int, int) ItemPair(int itemId)
    {
        EnsureLoaded();
        if (items.TryGetValue(itemId, out var p)) return p;
        int baseId = itemId / 1000 * 1000;
        return baseId != itemId && items.TryGetValue(baseId, out var b) ? b : (0, 0);
    }

    public static int FxSound(int fxId)
    {
        EnsureLoaded();
        return fxId > 0 && fxById.TryGetValue(fxId, out int s) ? s : 0;
    }

    public static int FxSound(string fxName)
    {
        EnsureLoaded();
        return fxName.Length > 0 && fxByName.TryGetValue(fxName.ToLowerInvariant(), out int s) ? s : 0;
    }

    public static ZoneBgm? Zone(int zoneId)
    {
        EnsureLoaded();
        return zones.TryGetValue(zoneId, out var z) ? z : null;
    }

    public static Footsteps? Steps(int raceId)
    {
        EnsureLoaded();
        return steps.TryGetValue(raceId, out var s) ? s : null;
    }

    private static IEnumerable<(string, Variant)> Each(Godot.Collections.Dictionary root, string section)
    {
        if (!root.ContainsKey(section) || root[section].VariantType != Variant.Type.Dictionary)
            yield break;
        var d = root[section].AsGodotDictionary();
        foreach (var k in d.Keys)
            yield return (k.AsString(), d[k]);
    }

    private static int Int(Godot.Collections.Dictionary d, string k) =>
        d.ContainsKey(k) ? d[k].AsInt32() : 0;

    private static string Str(Godot.Collections.Dictionary d, string k) =>
        d.ContainsKey(k) ? d[k].AsString() : "";
}
