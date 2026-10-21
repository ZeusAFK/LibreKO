using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public static partial class CharacterPreview
{
    private static Dictionary<int, string>? bodyIndex;
    private static Dictionary<int, int>? armorItems;
    private static Dictionary<int, (string Face, string Hair)>? faceRaces;

    private const string BodyIndexPath = "res://assets/characters/index.json";

    private static readonly (int Gear, int Part)[] ArmorSlotToPart =
        { (0, 0), (1, 1), (3, 2), (4, 3), (2, 5) };

    public static Node3D? Build(int race, int face, int[]? gear, int hair = 0, Color? hairColour = null,
                                bool enableShine = false)
    {
        var scene = ResolveBody(race);
        if (scene == null) return null;
        var body = scene.Instantiate<Node3D>();
        SkinShare.ShareUnder(body);
        Graft(body, race, face, gear, hair, hairColour, enableShine);
        AttachWeapons(body, race, gear, enableShine);
        foreach (var wingAnim in WingRig.AttachWings(body, gear, race, 0, enableShine))
        {
            if (wingAnim == null) continue;
            string? wingClip = null;
            WingRig.PlayWingClip(wingAnim, ref wingClip, WingState.Breath);
        }
        HandFxRig.AttachHandFx(body, gear, race, 0);
        ForceDoubleSided(body);
        var ap = ModelNodes.BodyAnim(body);
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

    public static void Warm()
    {
        bodyIndex ??= LoadIdIndex(BodyIndexPath);
        LoadParts();
        _ = WeaponCatalog.Index;
    }

    private static PackedScene? ResolveBody(int race)
    {
        bodyIndex ??= LoadIdIndex(BodyIndexPath);
        if (!bodyIndex.TryGetValue(race, out var stem)) return null;
        string path = $"res://assets/characters/{stem}.glb";
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null;
    }

    private const int HairPartNode = 5;

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

        var costume = gear != null
            ? Domain.CostumeLook.Dress(gear, false, ArmorResource,
                (part, resource, itemId) => GraftArmor(parts, part, itemId, resource, race))
            : null;
        bool helmet = costume?.Contains(Domain.CostumeLook.HeadPart) == true
                      || gear != null && gear.Length > 2 && gear[2] > 0;
        if (!helmet && HairPartStem(race, hair) is { } hairStem
            && GraftPart(parts, HairPartNode, hairStem, "characters") && hairColour is { } tint)
            TintHair(parts[HairPartNode], tint);

        if (gear != null)
            foreach (var (gi, pn) in ArmorSlotToPart)
            {
                if (gi >= gear.Length || gear[gi] <= 0) continue;
                if (costume?.Contains(pn) == true) continue;
                int resource = ArmorResource(gear[gi]);
                if (resource != 0 && GraftArmor(parts, pn, gear[gi], resource, race) && enableShine)
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
        if (src?.Mesh != null)
        {
            target.Mesh = src.Mesh;
            target.Skin = SkinShare.Canonical(src.Skin);
            SkinShare.FixBounds(target);
            target.Visible = true;
            ok = true;
        }
        inst.QueueFree();
        return ok;
    }

    private static int PartNodeIndex(string nodeName) => Presentation.ModelParts.PartNodeIndex(nodeName);

    private static int ArmorResource(int itemId) =>
        armorItems != null
        && (armorItems.TryGetValue(itemId, out int r) || armorItems.TryGetValue(Domain.ItemData.BaseId(itemId), out r))
            ? r : 0;

    private static bool GraftArmor(Dictionary<int, MeshInstance3D> parts, int partNode, int itemId, int resource, int race)
    {
        int prefix = Domain.CostumeLook.PartPrefix(itemId, Domain.ItemData.Get(itemId)?.SaleType ?? 0);
        foreach (string stem in Domain.CostumeLook.PartStems(prefix, resource, race))
            if (GraftPart(parts, partNode, stem, "items/armor")) return true;
        return false;
    }

    private static string? FacePartStem(int race, int face)
    {
        if (faceRaces == null || !faceRaces.TryGetValue(race, out var rr) || rr.Face.Length == 0) return null;
        return FaceStem(rr.Face, face);
    }

    internal static string FaceStem(string racePrefix, int face) => HairStem(racePrefix, face);

    private static string? HairPartStem(int race, int hair)
    {
        LoadParts();
        if (faceRaces == null || !faceRaces.TryGetValue(race, out var rr) || rr.Hair.Length == 0) return null;
        return HairStem(rr.Hair, hair);
    }

    internal static string HairStem(string racePrefix, int style)
    {
        string stem = $"{racePrefix}{style:D2}";
        return ResourceLoader.Exists($"res://assets/characters/{stem}.glb") ? stem : $"{racePrefix}00";
    }

    public static bool RetintHair(Node3D body, Color tint)
    {
        foreach (var mi in PartMeshes(body))
        {
            if (PartNodeIndex(mi.Name) != HairPartNode || !mi.Visible) continue;
            TintHair(mi, tint);
            return true;
        }
        return false;
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
        if (faceRaces == null || !faceRaces.TryGetValue(race, out var rr)) return 0;
        string stem = face ? rr.Face : rr.Hair;
        if (stem.Length == 0) return 0;
        int n = 0;
        while (n < 32 && ResourceLoader.Exists($"res://assets/characters/{stem}{n:D2}.glb")) n++;
        return n;
    }

    private static void LoadParts()
    {
        if (armorItems != null) return;
        armorItems = new Dictionary<int, int>();
        faceRaces = new Dictionary<int, (string, string)>();
        using (var f = Godot.FileAccess.Open("res://assets/items/armor/index.json", Godot.FileAccess.ModeFlags.Read))
            if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } root
                && root.TryGetValue("items", out var iv) && iv.AsGodotDictionary() is { } items)
                foreach (var k in items.Keys)
                    if (int.TryParse(k.AsString(), out int id)) armorItems[id] = items[k].AsInt32();
        using (var f = Godot.FileAccess.Open("res://assets/characters/faces.json", Godot.FileAccess.ModeFlags.Read))
            if (f != null && Json.ParseString(f.GetAsText()).AsGodotDictionary() is { } root
                && root.TryGetValue("races", out var rv) && rv.AsGodotDictionary() is { } races)
                foreach (var k in races.Keys)
                    if (int.TryParse(k.AsString(), out int rid) && races[k].AsGodotDictionary() is { } e)
                        faceRaces[rid] = (e.TryGetValue("facePart", out var fp) ? fp.AsString() : "",
                                           e.TryGetValue("hairPart", out var hp) ? hp.AsString() : "");
    }

    private static void AttachWeapons(Node3D body, int race, int[]? gear, bool enableShine)
    {
        if (gear == null) return;
        var skel = FindFirst<Skeleton3D>(body);
        if (skel == null) return;
        float bindHeight = WeaponMount.BindHeight(skel);
        var wearer = new HandWearer(race);
        foreach (int slot in new[] { InventoryConstants.VisRightHand, InventoryConstants.VisLeftHand })
        {
            if (slot >= gear.Length || gear[slot] <= 0) continue;
            if (!TryGetWeapon(gear[slot], out var w)) continue;
            string path = $"res://assets/items/weapon/{w.Stem}.glb";
            if (!ResourceLoader.Exists(path) || ResourceLoader.Load(path) is not PackedScene scene) continue;
            bool right = slot == InventoryConstants.VisRightHand;
            int bone = WeaponMount.Bone(skel, right, gear[slot]);
            if (bone < 0) continue;
            var attach = new BoneAttachment3D { Name = $"weapon_{slot}", BoneIdx = bone };
            skel.AddChild(attach);
            var mesh = scene.Instantiate<Node3D>();
            mesh.Transform = WeaponMount.HandPlugLocal(w.Pose, w.Version, w.RaceBlocks, right, wearer, bindHeight);
            attach.AddChild(mesh);
            WeaponIdle.Start(mesh, w.Anim);
            WeaponGlow.Attach(mesh, gear[slot]);
            if (enableShine) ItemShine.Apply(mesh, gear[slot], slot);
        }
    }

    private static bool TryGetWeapon(int itemId, out WeaponCatalog.Info weapon)
    {
        var weapons = WeaponCatalog.Index;
        return weapons.TryGetValue(itemId, out weapon!)
            || weapons.TryGetValue(Domain.ItemData.BaseId(itemId), out weapon!);
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
