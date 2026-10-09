using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

internal static class WeaponCatalog
{
    private const string IndexPath = "res://assets/items/weapon/index.json";
    private const string AliasesPath = "res://assets/items/weapon/visual_aliases.json";
    private const string GlowPath = "res://assets/items/weapon/glow.json";
    private const int MaxExtOffset = 9999;

    internal sealed class Info
    {
        public string Stem = "";
        public int Joint;
        public Vector3 Pos;
        public Quaternion Quat = Quaternion.Identity;
        public Vector3 Scale = Vector3.One;
        public Vector3? FxPos;
        public float FxRadius;
        public string FxGuide = "";
        public int TraceSteps;
        public uint TraceColor = WeaponGlowRule.White;
        public float Trace0, Trace1;
    }

    private static Dictionary<int, Info>? _index;
    private static Dictionary<int, int>? _weaponCat;
    private static Dictionary<int, Dictionary<int, string>>? _glowCats;
    private static Dictionary<int, Dictionary<int, string>>? _glowTails;
    private static bool _glowLoaded;

    internal static IReadOnlyDictionary<int, Info> Index => _index ??= LoadIndex();

    internal static int ResolveBaseId(int itemId)
    {
        var index = Index;
        if (index.ContainsKey(itemId)) return itemId;

        int rounded = ItemData.BaseId(itemId);
        if (index.ContainsKey(rounded)) return rounded;
        if (_weaponCat != null && _weaponCat.ContainsKey(rounded)) return rounded;

        int bestBase = 0, bestExt = int.MaxValue;
        foreach (var baseId in index.Keys)
        {
            int ext = itemId - baseId;
            if (ext < 0 || ext > MaxExtOffset || ext >= bestExt) continue;
            bestBase = baseId;
            bestExt = ext;
        }
        if (bestBase != 0) return bestBase;

        if (_weaponCat != null)
        {
            foreach (var baseId in _weaponCat.Keys)
            {
                int ext = itemId - baseId;
                if (ext < 0 || ext > MaxExtOffset || ext >= bestExt) continue;
                bestBase = baseId;
                bestExt = ext;
            }
        }
        return bestBase != 0 ? bestBase : rounded;
    }

    internal static bool TryResolveGlow(int itemId, out int baseItemId, out string fxName, out string tailFx)
    {
        if (!_glowLoaded) { LoadGlow(); _glowLoaded = true; }
        tailFx = "";
        baseItemId = ResolveBaseId(itemId);
        if (TryGlowFor(baseItemId, out fxName))
        {
            tailFx = TailFor(baseItemId);
            return true;
        }

        int bestBase = 0, bestExt = int.MaxValue;
        string? bestFx = null;
        if (_weaponCat != null)
        {
            foreach (var candidate in _weaponCat.Keys)
            {
                int ext = itemId - candidate;
                if (ext < 0 || ext > MaxExtOffset || ext >= bestExt) continue;
                if (!TryGlowFor(candidate, out var candidateFx)) continue;
                bestBase = candidate;
                bestExt = ext;
                bestFx = candidateFx;
            }
        }
        if (bestFx == null) return false;
        baseItemId = bestBase;
        fxName = bestFx;
        tailFx = TailFor(bestBase);
        return true;

        string TailFor(int candidateBase)
        {
            int ext = itemId - candidateBase;
            if (_weaponCat != null && _glowTails != null
                && _weaponCat.TryGetValue(candidateBase, out int cat)
                && _glowTails.TryGetValue(cat, out var exts)
                && exts.TryGetValue(ext, out var tail))
                return tail;
            return "";
        }

        bool TryGlowFor(int candidateBase, out string resolvedFx)
        {
            resolvedFx = "";
            if (_weaponCat == null || _glowCats == null) return false;
            int ext = itemId - candidateBase;
            if (ext < 0 || ext > MaxExtOffset) return false;
            if (_weaponCat.TryGetValue(candidateBase, out int cat)
                && _glowCats.TryGetValue(cat, out var exts)
                && exts.TryGetValue(ext, out var fx))
            {
                resolvedFx = fx;
                return true;
            }
            return false;
        }
    }

    private static void LoadGlow()
    {
        _weaponCat = new Dictionary<int, int>();
        _glowCats = new Dictionary<int, Dictionary<int, string>>();
        _glowTails = new Dictionary<int, Dictionary<int, string>>();
        using var f = Godot.FileAccess.Open(GlowPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is not { } root) return;
        if (root.TryGetValue("weaponCat", out var wc) && wc.AsGodotDictionary() is { } wcd)
            foreach (var k in wcd.Keys)
                if (int.TryParse(k.AsString(), out int baseId)) _weaponCat[baseId] = wcd[k].AsInt32();
        LoadGlowSection(root, "cats", _glowCats);
        LoadGlowSection(root, "tails", _glowTails);
    }

    private static void LoadGlowSection(Godot.Collections.Dictionary root, string key,
        Dictionary<int, Dictionary<int, string>> into)
    {
        if (!root.TryGetValue(key, out var cs) || cs.AsGodotDictionary() is not { } csd) return;
        foreach (var k in csd.Keys)
        {
            if (!int.TryParse(k.AsString(), out int cat) || csd[k].AsGodotDictionary() is not { } extd) continue;
            var map = new Dictionary<int, string>();
            foreach (var e in extd.Keys)
                if (int.TryParse(e.AsString(), out int ext)) map[ext] = extd[e].AsString();
            into[cat] = map;
        }
    }

    private static Dictionary<int, Info> LoadIndex()
    {
        var map = new Dictionary<int, Info>();
        using var f = Godot.FileAccess.Open(IndexPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return map;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } d)
            foreach (var k in d.Keys)
            {
                if (!int.TryParse(k.AsString(), out int id)) continue;
                var e = d[k].AsGodotDictionary();
                var pos = e["pos"].AsGodotArray();
                var q = e["quat"].AsGodotArray();
                var sc = e["scale"].AsGodotArray();
                var wi = new Info
                {
                    Stem = e["stem"].AsString(),
                    Joint = e["joint"].AsInt32(),
                    Pos = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]),
                    Quat = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]),
                    Scale = new Vector3((float)sc[0], (float)sc[1], (float)sc[2]),
                };
                if (e.ContainsKey("fxp"))
                {
                    var fp = e["fxp"].AsGodotArray();
                    wi.FxPos = new Vector3((float)fp[0], (float)fp[1], (float)fp[2]);
                    wi.FxRadius = (float)e["fxr"].AsDouble();
                }
                if (e.ContainsKey("fxg"))
                    wi.FxGuide = e["fxg"].AsString();
                if (e.ContainsKey("trstep"))
                {
                    wi.TraceSteps = e["trstep"].AsInt32();
                    wi.TraceColor = (uint)e["trcol"].AsInt64();
                    wi.Trace0 = (float)e["tr0"].AsDouble();
                    wi.Trace1 = (float)e["tr1"].AsDouble();
                }
                map[id] = wi;
            }

        using (var aliasesFile = Godot.FileAccess.Open(AliasesPath, Godot.FileAccess.ModeFlags.Read))
            if (aliasesFile != null)
            {
                var parsed = Json.ParseString(aliasesFile.GetAsText());
                if (parsed.VariantType == Variant.Type.Dictionary)
                {
                    var aliases = parsed.AsGodotDictionary();
                    foreach (var key in aliases.Keys)
                        if (int.TryParse(key.AsString(), out int exactId)
                            && map.TryGetValue(aliases[key].AsInt32(), out var canonical))
                            map[exactId] = canonical;
                }
            }
        return map;
    }
}
