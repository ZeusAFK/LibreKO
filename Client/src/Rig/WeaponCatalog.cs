using System.Collections.Generic;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

internal static class WeaponCatalog
{
    private const string IndexPath = "res://assets/items/weapon/index.json";
    private const string AliasesPath = "res://assets/items/weapon/visual_aliases.json";
    private const string GlowPath = "res://assets/items/weapon/glow.json";
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
    private static Dictionary<int, Dictionary<int, string>>? glowCats;
    private static Dictionary<int, Dictionary<int, string>>? glowTails;
    private static bool glowLoaded;

    internal static IReadOnlyDictionary<int, Info> Index => index ??= LoadIndex();

    internal static int ResolveBaseId(int itemId) =>
        Index.ContainsKey(itemId) ? itemId : ItemData.BaseId(itemId);

    internal static bool TryResolveGlow(int itemId, out int baseItemId, out string fxName, out string tailFx)
    {
        if (!glowLoaded) { LoadGlow(); glowLoaded = true; }
        baseItemId = ResolveBaseId(itemId);
        fxName = "";
        tailFx = "";
        if (ItemData.Get(itemId) is not { } def || glowCats == null
            || !glowCats.TryGetValue(def.Cat, out var exts)) return false;
        int ext = ItemData.ExtIdFor(itemId);
        if (!exts.TryGetValue(ext, out var fx)) return false;
        fxName = fx;
        if (glowTails != null && glowTails.TryGetValue(def.Cat, out var tails) && tails.TryGetValue(ext, out var tail))
            tailFx = tail;
        return true;
    }

    private static void LoadGlow()
    {
        glowCats = new Dictionary<int, Dictionary<int, string>>();
        glowTails = new Dictionary<int, Dictionary<int, string>>();
        using var f = Godot.FileAccess.Open(GlowPath, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is not { } root) return;
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
