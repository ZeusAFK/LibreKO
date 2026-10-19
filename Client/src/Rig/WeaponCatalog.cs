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
    private const uint DefaultTraceColor = 0xFFFFFFFF;

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
        public uint TraceColor = DefaultTraceColor;
        public float Trace0, Trace1;
        public string Anim = "";
        public int Version;
        public PlugPose[] RaceBlocks = System.Array.Empty<PlugPose>();

        public PlugPose Pose => new(Pos, Quat, Scale);
    }

    internal static Vector3 ReadVector(Variant value)
    {
        var a = value.AsGodotArray();
        return new Vector3((float)a[0], (float)a[1], (float)a[2]);
    }

    internal static Quaternion ReadQuaternion(Variant value)
    {
        var a = value.AsGodotArray();
        return new Quaternion((float)a[0], (float)a[1], (float)a[2], (float)a[3]);
    }

    internal static int ReadVersion(Godot.Collections.Dictionary entry) =>
        entry.TryGetValue("ver", out var version) ? version.AsInt32() : 0;

    internal static PlugPose[] ReadRaceBlocks(Godot.Collections.Dictionary entry)
    {
        if (!entry.TryGetValue("race", out var race) || race.VariantType != Variant.Type.Array)
            return System.Array.Empty<PlugPose>();
        var blocks = race.AsGodotArray();
        var poses = new PlugPose[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i].AsGodotDictionary();
            poses[i] = new PlugPose(ReadVector(b["pos"]), ReadQuaternion(b["quat"]), ReadVector(b["scale"]));
        }
        return poses;
    }

    private static Dictionary<int, Info>? index;
    private static Dictionary<int, int>? weaponCat;
    private static Dictionary<int, Dictionary<int, string>>? glowCats;
    private static Dictionary<int, Dictionary<int, string>>? glowTails;
    private static bool glowLoaded;

    internal static IReadOnlyDictionary<int, Info> Index => index ??= LoadIndex();

    internal static int ResolveBaseId(int itemId)
    {
        var byItem = Index;
        if (byItem.ContainsKey(itemId)) return itemId;

        int rounded = ItemData.BaseId(itemId);
        if (byItem.ContainsKey(rounded)) return rounded;
        if (weaponCat != null && weaponCat.ContainsKey(rounded)) return rounded;

        int bestBase = 0, bestExt = int.MaxValue;
        foreach (var baseId in byItem.Keys)
        {
            int ext = itemId - baseId;
            if (ext < 0 || ext > MaxExtOffset || ext >= bestExt) continue;
            bestBase = baseId;
            bestExt = ext;
        }
        if (bestBase != 0) return bestBase;

        if (weaponCat != null)
        {
            foreach (var baseId in weaponCat.Keys)
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
        if (!glowLoaded) { LoadGlow(); glowLoaded = true; }
        tailFx = "";
        baseItemId = ResolveBaseId(itemId);
        if (TryGlowFor(baseItemId, out fxName))
        {
            tailFx = TailFor(baseItemId);
            return true;
        }

        int bestBase = 0, bestExt = int.MaxValue;
        string? bestFx = null;
        if (weaponCat != null)
        {
            foreach (var candidate in weaponCat.Keys)
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
            if (weaponCat != null && glowTails != null
                && weaponCat.TryGetValue(candidateBase, out int cat)
                && glowTails.TryGetValue(cat, out var exts)
                && exts.TryGetValue(ext, out var tail))
                return tail;
            return "";
        }

        bool TryGlowFor(int candidateBase, out string resolvedFx)
        {
            resolvedFx = "";
            if (weaponCat == null || glowCats == null) return false;
            int ext = itemId - candidateBase;
            if (ext < 0 || ext > MaxExtOffset) return false;
            if (weaponCat.TryGetValue(candidateBase, out int cat)
                && glowCats.TryGetValue(cat, out var exts)
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
        weaponCat = new Dictionary<int, int>();
        glowCats = new Dictionary<int, Dictionary<int, string>>();
        glowTails = new Dictionary<int, Dictionary<int, string>>();
        using var f = Godot.FileAccess.Open(GlowPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is not { } root) return;
        if (root.TryGetValue("weaponCat", out var wc) && wc.AsGodotDictionary() is { } wcd)
            foreach (var k in wcd.Keys)
                if (int.TryParse(k.AsString(), out int baseId)) weaponCat[baseId] = wcd[k].AsInt32();
        LoadGlowSection(root, "cats", glowCats);
        LoadGlowSection(root, "tails", glowTails);
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
                if (e.ContainsKey("anim"))
                    wi.Anim = e["anim"].AsString();
                wi.Version = ReadVersion(e);
                wi.RaceBlocks = ReadRaceBlocks(e);
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
