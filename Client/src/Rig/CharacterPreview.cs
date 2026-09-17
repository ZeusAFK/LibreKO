using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static partial class CharacterPreview
{
    private static Dictionary<int, string>? _bodyIndex;
    private static Dictionary<int, int>? _armorItems;
    private static Dictionary<int, (string Face, string Hair)>? _faceRaces;
    private static Dictionary<int, WeaponPlug>? _weapons;

    private static readonly (int Gear, int Part)[] ArmorSlotToPart =
        { (0, 0), (1, 1), (3, 2), (4, 3), (2, 5) };

    private sealed class WeaponPlug { public string Stem = ""; public Vector3 Pos; public Quaternion Quat = Quaternion.Identity; public Vector3 Scale = Vector3.One; }

    public static Node3D? Build(int race, int face, int[]? gear, int hair = 0, Color? hairColour = null,
                                bool enableShine = false)
    {
        var scene = ResolveBody(race);
        if (scene == null) return null;
        var body = scene.Instantiate<Node3D>();
        Graft(body, race, face, gear, hair, hairColour, enableShine);
        AttachWeapons(body, gear, enableShine);
        foreach (var wingAnim in World.AttachWings(body, gear, race, 0, enableShine))
        {
            if (wingAnim == null) continue;
            string? wingClip = null;
            World.PlayWingClip(wingAnim, ref wingClip, World.WingState.Breath);
        }
        World.AttachHandFx(body, gear, race, 0);
        ForceDoubleSided(body);
        var ap = World.BodyAnim(body);
        if (ap != null)
        {
            string[] idleCandidates = { "basic", "breath", "base", "stand", "wait", "idle" };
            string clip = "";
            foreach (string candidate in idleCandidates)
                if (ap.HasAnimation(candidate))
                {
                    clip = candidate;
                    break;
                }
            if (clip.Length > 0)
            {
                if (ap.GetAnimation(clip) is { } animation)
                    animation.LoopMode = Animation.LoopModeEnum.Linear;
                ap.Play(clip);
                ap.Advance(0);
            }
        }
        return body;
    }

    private static List<MeshInstance3D> PartMeshes(Node body)
    {
        var list = new List<MeshInstance3D>();
        void Walk(Node x)
        {
            foreach (var c in x.GetChildren())
            {
                if (c is BoneAttachment3D) continue;
                if (c is MeshInstance3D mi) list.Add(mi);
                Walk(c);
            }
        }
        Walk(body);
        return list;
    }

    private static PackedScene? ResolveBody(int race)
    {
        _bodyIndex ??= LoadIdIndex("res://assets/characters/index.json");
        if (!_bodyIndex.TryGetValue(race, out var stem)) return null;
        string path = $"res://assets/characters/{stem}.glb";
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
    }

    private static void Graft(Node3D body, int race, int face, int[]? gear, int hair, Color? hairColour, bool enableShine)
    {
        LoadParts();
        var parts = new Dictionary<int, MeshInstance3D>();
        foreach (var mi in PartMeshes(body))
        {
            int idx = PartNodeIndex(mi.Name);
            if (idx >= 0) parts[idx] = mi;
        }

        string? faceStem = FacePartStem(race, face);
        if (faceStem != null) GraftPart(parts, 4, faceStem, "characters");

        bool helmet = gear != null && gear.Length > 2 && gear[2] > 0;
        if (!helmet && HairPartStem(race, hair) is { } hairStem
            && GraftPart(parts, 5, hairStem, "characters") && hairColour is { } tint)
            TintHair(parts[5], tint);

        if (gear != null)
            foreach (var (gi, pn) in ArmorSlotToPart)
            {
                if (gi >= gear.Length || gear[gi] <= 0) continue;
                string? stem = ArmorPartStem(gear[gi], race);
                if (stem != null && GraftPart(parts, pn, stem, "items/armor") && enableShine)
                    ItemShine.Apply(parts[pn], gear[gi], pn);
            }
    }

    private static bool GraftPart(Dictionary<int, MeshInstance3D> parts, int partNode, string stem, string dir)
    {
        if (!parts.TryGetValue(partNode, out var target)) return false;
        string path = $"res://assets/{dir}/{stem}.glb";
        if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene) return false;
        var inst = scene.Instantiate<Node3D>();
        var src = FindFirst<MeshInstance3D>(inst);
        bool ok = false;
        if (src?.Mesh != null) { target.Mesh = src.Mesh; target.Skin = src.Skin; target.Visible = true; ok = true; }
        inst.QueueFree();
        return ok;
    }

    private static int PartNodeIndex(string nodeName)
    {
        int i = nodeName.LastIndexOf("_part", StringComparison.Ordinal);
        if (i < 0) return -1;
        return int.TryParse(nodeName.AsSpan(i + 5), out int n) ? n : -1;
    }

    private static string? ArmorPartStem(int itemId, int race)
    {
        if (_armorItems == null) return null;
        if (!_armorItems.TryGetValue(itemId, out int r)
            && !_armorItems.TryGetValue(itemId / 1000 * 1000, out r))
            return null;
        int cat = r / 10000000, mid = (r / 1000) % 10000 + race, type = (r / 10) % 100, variant = r % 10;
        return $"{cat}_{mid:D4}_{type:D2}_{variant}";
    }

    private static string? FacePartStem(int race, int face)
    {
        if (_faceRaces == null || !_faceRaces.TryGetValue(race, out var rr) || rr.Face.Length == 0) return null;
        return $"{rr.Face}{face:D2}";
    }

    private static string? HairPartStem(int race, int hair)
    {
        LoadParts();
        if (_faceRaces == null || !_faceRaces.TryGetValue(race, out var rr) || rr.Hair.Length == 0) return null;
        return HairStem(rr.Hair, hair);
    }

    internal static string HairStem(string racePrefix, int style)
    {
        string stem = $"{racePrefix}{style:D2}";
        return ResourceLoader.Exists($"res://assets/characters/{stem}.glb") ? stem : $"{racePrefix}00";
    }

    internal static void TintHair(MeshInstance3D part, Color tint)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = tint,
            Roughness = 0.85f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        if (part.Mesh?.SurfaceGetMaterial(0) is StandardMaterial3D src && src.AlbedoTexture != null)
        {
            mat.AlbedoTexture = src.AlbedoTexture;
            mat.Transparency = src.Transparency;
            mat.AlphaScissorThreshold = src.AlphaScissorThreshold;
            mat.CullMode = src.CullMode;
        }
        part.MaterialOverride = mat;
    }

    public static int FaceCount(int race) => VariantCount(race, face: true);

    public static int HairCount(int race) => VariantCount(race, face: false);

    private static int VariantCount(int race, bool face)
    {
        LoadParts();
        if (_faceRaces == null || !_faceRaces.TryGetValue(race, out var rr)) return 0;
        string stem = face ? rr.Face : rr.Hair;
        if (stem.Length == 0) return 0;
        int n = 0;
        while (n < 32 && ResourceLoader.Exists($"res://assets/characters/{stem}{n:D2}.glb")) n++;
        return n;
    }

    private static void LoadParts()
    {
        if (_armorItems != null) return;
        _armorItems = new Dictionary<int, int>();
        _faceRaces = new Dictionary<int, (string, string)>();
        using (var f = Godot.FileAccess.Open("res://assets/items/armor/index.json", Godot.FileAccess.ModeFlags.Read))
            if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } root
                && root.TryGetValue("items", out var iv) && iv.AsGodotDictionary() is { } items)
                foreach (var k in items.Keys)
                    if (int.TryParse(k.AsString(), out int id)) _armorItems[id] = items[k].AsInt32();
        using (var f = Godot.FileAccess.Open("res://assets/characters/faces.json", Godot.FileAccess.ModeFlags.Read))
            if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } root
                && root.TryGetValue("races", out var rv) && rv.AsGodotDictionary() is { } races)
                foreach (var k in races.Keys)
                    if (int.TryParse(k.AsString(), out int rid) && races[k].AsGodotDictionary() is { } e)
                        _faceRaces[rid] = (e.TryGetValue("facePart", out var fp) ? fp.AsString() : "",
                                           e.TryGetValue("hairPart", out var hp) ? hp.AsString() : "");
    }

    private static void AttachWeapons(Node3D body, int[]? gear, bool enableShine)
    {
        if (gear == null) return;
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return;
        _weapons ??= LoadWeapons();
        foreach (int slot in new[] { InventoryConstants.VisRightHand, InventoryConstants.VisLeftHand })
        {
            if (slot >= gear.Length || gear[slot] <= 0) continue;
            if (!TryGetWeapon(gear[slot], out var w)) continue;
            string path = $"res://assets/items/weapon/{w.Stem}.glb";
            if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene) continue;
            int bone = HandBone(skel, right: slot == 6);
            if (bone < 0) continue;
            var attach = new BoneAttachment3D { Name = $"weapon_{slot}", BoneIdx = bone };
            skel.AddChild(attach);
            var mesh = scene.Instantiate<Node3D>();
            mesh.Transform = new Transform3D(new Basis(w.Quat).Scaled(w.Scale), w.Pos);
            attach.AddChild(mesh);
            if (enableShine) ItemShine.Apply(mesh, gear[slot], slot);
        }
    }

    private static int HandBone(Skeleton3D skel, bool right)
    {
        string wristKey = right ? "rightwrist" : "leftwrist";
        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            if (skel.GetBoneName(i).Replace(" ", "").ToLower() != wristKey) continue;
            var kids = skel.GetBoneChildren(i);
            return kids.Length > 0 ? kids[0] : i;
        }
        return -1;
    }

    private static Dictionary<int, WeaponPlug> LoadWeapons()
    {
        var map = new Dictionary<int, WeaponPlug>();
        using var f = Godot.FileAccess.Open("res://assets/items/weapon/index.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) return map;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } d)
            foreach (var k in d.Keys)
            {
                if (!int.TryParse(k.AsString(), out int id)) continue;
                var e = d[k].AsGodotDictionary();
                var pos = e["pos"].AsGodotArray(); var q = e["quat"].AsGodotArray(); var sc = e["scale"].AsGodotArray();
                map[id] = new WeaponPlug
                {
                    Stem = e["stem"].AsString(),
                    Pos = new Vector3((float)pos[0], (float)pos[1], (float)pos[2]),
                    Quat = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]),
                    Scale = new Vector3((float)sc[0], (float)sc[1], (float)sc[2]),
                };
            }

        using (var aliasesFile = Godot.FileAccess.Open(
                   "res://assets/items/weapon/visual_aliases.json",
                   Godot.FileAccess.ModeFlags.Read))
            if (aliasesFile != null)
            {
                var parsed = Json.ParseString(aliasesFile.GetAsText());
                if (parsed.VariantType == Variant.Type.Dictionary)
                {
                    var aliases = parsed.AsGodotDictionary();
                    foreach (var key in aliases.Keys)
                    {
                        if (int.TryParse(key.AsString(), out int exactId)
                            && map.TryGetValue(aliases[key].AsInt32(), out var canonical))
                            map[exactId] = canonical;
                    }
                }
            }
        return map;
    }

    private static bool TryGetWeapon(int itemId, out WeaponPlug weapon)
    {
        _weapons ??= LoadWeapons();
        return _weapons.TryGetValue(itemId, out weapon!)
            || _weapons.TryGetValue(itemId / 1000 * 1000, out weapon!);
    }

    private static Dictionary<int, string> LoadIdIndex(string path)
    {
        var map = new Dictionary<int, string>();
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null) return map;
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } d)
            foreach (var k in d.Keys)
                if (int.TryParse(k.AsString(), out int id)) map[id] = d[k].AsString();
        return map;
    }

    private static void ForceDoubleSided(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                if (mi.GetActiveMaterial(s) is BaseMaterial3D m) m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        foreach (var c in n.GetChildren()) ForceDoubleSided(c);
    }

    private static T? FindFirst<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren()) { var r = FindFirst<T>(c); if (r != null) return r; }
        return null;
    }

    private static List<T> FindAll<T>(Node n) where T : class
    {
        var outp = new List<T>();
        void Rec(Node x) { if (x is T t) outp.Add(t); foreach (var c in x.GetChildren()) Rec(c); }
        Rec(n);
        return outp;
    }
}
