using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    internal static void AttachCharacterFxPlugs(Node3D body, string modelStem)
    {
        if (string.IsNullOrEmpty(modelStem)) return;
        string path = $"res://assets/npcs/{modelStem}.fxplug.json";
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return;
        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return;
        var root = parsed.AsGodotDictionary();
        if (!root.TryGetValue("parts", out var pv) || pv.VariantType != Variant.Type.Array) return;

        var skeleton = FindFirst<Skeleton3D>(body);
        foreach (var value in pv.AsGodotArray())
        {
            if (value.VariantType != Variant.Type.Dictionary) continue;
            var part = value.AsGodotDictionary();
            string fx = part.TryGetValue("fx", out var fv) ? fv.AsString() : "";
            int bone = part.TryGetValue("bone", out var bv) ? bv.AsInt32() : -1;
            Vector3 pos = Vector3.Zero;
            if (part.TryGetValue("p", out var pp) && pp.VariantType == Variant.Type.Array)
            {
                var a = pp.AsGodotArray();
                if (a.Count >= 3)
                    pos = new Vector3((float)a[0].AsDouble(), (float)a[1].AsDouble(), (float)a[2].AsDouble());
            }
            Node3D parent = body;
            if (skeleton != null && bone >= 0 && bone < skeleton.GetBoneCount())
            {
                var attachment = new BoneAttachment3D
                {
                    Name = $"fxplug_{bone}_{fx}",
                    BoneIdx = bone,
                };
                skeleton.AddChild(attachment);
                parent = attachment;
            }
            float scale = part.TryGetValue("scale", out var sv) ? (float)sv.AsDouble() : 1f;
            if (!(scale > 0.0001f)) scale = 1f;
            var spawned = Fx.Spawn(fx, parent, pos, oneShot: false, sizeScale: scale);
            if (spawned is FxInstance pinned)
                pinned.Pin(parent, parent, pos, Fx.ReadVec3(part, "dir"), inheritScale: false);
            if (spawned != null) CullAttachedFx(spawned);
        }
    }

    private static void CullAttachedFx(Node node)
    {
        if (node is GeometryInstance3D gi)
        {
            gi.VisibilityRangeEnd = AttachedFxCullDist;
            gi.VisibilityRangeEndMargin = ObjectCullFade;
            gi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
            gi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            gi.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
        }
        foreach (var c in node.GetChildren()) CullAttachedFx(c);
    }

    private const float AttachedFxCullDist = 150f;

    private static float ModelTopY(Node node)
    {
        float top = 0f;
        if (node is MeshInstance3D mi && mi.Mesh != null)
        {
            var a = mi.Mesh.GetAabb();
            top = a.Position.Y + a.Size.Y;
        }
        foreach (var c in node.GetChildren())
            top = Mathf.Max(top, ModelTopY(c));
        return top;
    }

    private readonly System.Collections.Generic.Dictionary<int, PackedScene?> _mobSceneCache = new();
    private System.Collections.Generic.Dictionary<int, string>? _mobIndex;
    private readonly System.Collections.Generic.Dictionary<int, PackedScene?> _playerSceneCache = new();
    private System.Collections.Generic.Dictionary<int, string>? _playerIndex;
    private sealed class AnimMeta
    {
        public string Name = "";
        public float Start, End, Fps = 30f, Blend, Strike0, Strike1, TraceStart, TraceEnd;
        public float Sound0, Sound1;

        public float SoundTime(int slot) => EventTime(slot == 0 ? Sound0 : Sound1);

        public float StrikeTime(int slot) => EventTime(slot == 0 ? Strike0 : Strike1);

        public float TraceStartTime => EventTime(TraceStart);

        public float TraceEndTime => EventTime(TraceEnd);

        private float EventTime(float frame) =>
            frame < Start || frame > End || Fps <= 0f ? -1f : (frame - Start) / Fps;
    }
    private static readonly System.Collections.Generic.Dictionary<ulong,
        System.Collections.Generic.Dictionary<int, AnimMeta>> AnimationMetaByIndex = new();
    private static readonly System.Collections.Generic.Dictionary<ulong,
        System.Collections.Generic.Dictionary<string, AnimMeta>> AnimationMetaByName = new();

    private string? _sceneLoadInFlight;

    private bool SceneReadyFor(EntitySnapshot info)
    {
        if (info.ObjectType == NpcTypes.ObjectType.MapObject) return true;
        var cache = info.IsNpc ? _mobSceneCache : _playerSceneCache;
        int key = info.IsNpc ? info.ModelId : info.Race;
        if (cache.ContainsKey(key)) return true;
        _mobIndex ??= LoadIdIndex("res://assets/npcs/index.json");
        _playerIndex ??= LoadIdIndex("res://assets/characters/index.json");
        var index = info.IsNpc ? _mobIndex : _playerIndex;
        if (!index.TryGetValue(key, out var stem)) { cache[key] = null; return true; }
        string path = info.IsNpc ? $"res://assets/npcs/{stem}.glb" : $"res://assets/characters/{stem}.glb";
        if (!ResourceLoader.Exists(path)) { cache[key] = null; return true; }
        if (_sceneLoadInFlight == null)
        {
            if (ResourceLoader.LoadThreadedRequest(path) != Error.Ok) return true;
            _sceneLoadInFlight = path;
        }
        if (_sceneLoadInFlight != path) return false;
        var status = ResourceLoader.LoadThreadedGetStatus(path);
        if (status == ResourceLoader.ThreadLoadStatus.InProgress) return false;
        if (status == ResourceLoader.ThreadLoadStatus.Loaded)
            cache[key] = ResourceLoader.LoadThreadedGet(path) as PackedScene;
        _sceneLoadInFlight = null;
        return true;
    }

    private PackedScene? ResolveMobScene(int modelId)
    {
        if (_mobSceneCache.TryGetValue(modelId, out var cached)) return cached;
        _mobIndex ??= LoadIdIndex("res://assets/npcs/index.json");

        PackedScene? result = null;
        if (_mobIndex.TryGetValue(modelId, out var stem))
        {
            string resPath = $"res://assets/npcs/{stem}.glb";
            if (ResourceLoader.Exists(resPath)
                && ResourceLoader.Load(resPath) is PackedScene scene)
                result = scene;
        }
        _mobSceneCache[modelId] = result;
        return result;
    }

    private PackedScene? ResolvePlayerScene(int race)
    {
        if (_playerSceneCache.TryGetValue(race, out var cached)) return cached;
        _playerIndex ??= LoadIdIndex("res://assets/characters/index.json");

        PackedScene? result = null;
        if (_playerIndex.TryGetValue(race, out var stem))
        {
            string resPath = $"res://assets/characters/{stem}.glb";
            if (ResourceLoader.Exists(resPath)
                && ResourceLoader.Load(resPath) is PackedScene scene)
                result = scene;
        }
        _playerSceneCache[race] = result;
        return result;
    }

    private void RebuildSelfVisual()
    {
        if (_selfBody == null || !GodotObject.IsInstanceValid(_selfBody)) return;

        if (_selfVisual != null && GodotObject.IsInstanceValid(_selfVisual))
        {
            _selfBody.RemoveChild(_selfVisual);
            _selfVisual.QueueFree();
            _selfVisual = null!;
        }

        foreach (var child in _selfBody.GetChildren())
        {
            if (child is WeaponTrail wt)
            {
                _selfBody.RemoveChild(wt);
                wt.QueueFree();
            }
        }

        _selfRigAnim = null;
        _selfAnim = null;
        _selfFlinch = null;
        _selfClip = null;
        _selfNameTag = null;
        _selfPlate = null;

        string selfName = Net.I.LastEnter.Name is { Length: > 0 } n ? n : "You";
        PackedScene? selfScene = ResolvePlayerScene(_selfRace);
        Node3D selfVisual;
        int[] gear = SelfGear();

        if (selfScene != null)
        {
            (selfVisual, _selfAnim) = MakeAnimatedEntity(selfScene, selfName, 1f);
            _selfFlinch = Flinch.Attach(selfVisual);
            CaptureSelfDefaults(selfVisual);
            GraftEquipment(selfVisual, _selfRace, _selfFace, gear, _selfHair, Net.I.HelmetHidden);
        }
        else
        {
            selfVisual = MakeEntity(new Color(1f, 0.82f, 0.3f), selfName);
            selfVisual.Position = new Vector3(0, CapsuleHalf, 0);
        }

        _selfBody.AddChild(selfVisual);
        _selfVisual = selfVisual;
        _selfStandingVisualTransform = selfVisual.Transform;

        if (selfScene != null)
        {
            AttachWeapons(_self, gear);
            AttachClanGauntlet(_self, _selfRace, _myClan.InClan ? _myClan.Grade : 0);
            _selfWingAnims = AttachWings(_self, gear, _selfRace, _zone);
            System.Array.Clear(_selfWingClips);
            AttachHandFx(_self, gear, _selfRace, _zone);
            DressSelfCape();

            if (_selfAnim != null)
            {
                PlayClipOn(_selfAnim, ref _selfClip, "idle");
                _selfAnim.Advance(0.0);
            }
        }
    }

    private sealed class PartsIndex
    {
        public System.Collections.Generic.Dictionary<int, int> Items = new();
        public System.Collections.Generic.Dictionary<int, (string Face, string Hair)> Races = new();
    }
    private PartsIndex? _partsIndex;
    private bool _partsLoaded;
    private static readonly (int Gear, int Part)[] ArmorSlotToPart =
        { (0, 0), (1, 1), (3, 2), (4, 3), (2, 5) };

    private readonly System.Collections.Generic.Dictionary<string, (Mesh? Mesh, Skin? Skin)> _partCache = new();

    private sealed class WeaponInfo
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
        public uint TraceColor = 0xFFFFFFFF;
        public float Trace0, Trace1;
    }
    private System.Collections.Generic.Dictionary<int, WeaponInfo>? _weaponIndex;

    private System.Collections.Generic.Dictionary<int, int>? _weaponCat;
    private System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, string>>? _glowCats;
    private System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, string>>? _glowTails;
    private bool _glowLoaded;

    private static readonly System.Collections.Generic.HashSet<int> NoWeaponNpcIds = new()
    {
        8002, 8003,
        13013,
    };

    private bool TryResolveWeaponGlow(int itemId, out int baseItemId, out string fxName, out string tailFx)
    {
        tailFx = "";
        baseItemId = ResolveWeaponBaseId(itemId);
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
                if (ext < 0 || ext > 9999 || ext >= bestExt) continue;
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
            if (ext < 0 || ext > 9999) return false;
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

    private static readonly string[] IdleClips = { "basic", "breath", "base", "stand", "wait" };
    private static readonly string[] WalkClips = { "walk", "move", "run" };
    private static readonly string[] RunClips = { "run", "walk", "move" };
    private static readonly string[] WalkReverseClips = { "walk_reverse", "walk_back", "backward", "walk", "move" };
    private static readonly string[] SitClips = { "SitDown_Breath", "sit", "sitting", "seat", "rest" };
    private static readonly string[] SitDownClips = { "SitDown", "sit_down", "sit" };
    private static readonly string[] StandUpClips = { "StandUp", "0StandUp", "stand_up", "stand" };

    private static readonly string[] SwordIdleClips = { "breath_sword0", "breath_sword1", "breath" };
    private static readonly string[] DaggerIdleClips = { "breath_Dagger", "breath_Dagger1", "breath" };
    private static readonly string[] DualIdleClips = { "breath_dual0", "breath_dual1", "breath" };
    private static readonly string[] TwoHandIdleClips = { "breath_TwoHand0", "breath_TwoHand1", "breath" };
    private static readonly string[] BluntIdleClips = { "breath_blunt0", "breath_blunt1", "breath" };
    private static readonly string[] TwoBluntIdleClips = { "breath_Twoblunt0", "breath_Twoblunt1", "breath" };
    private static readonly string[] AxeIdleClips = { "breath_Axe0", "breath_Axe1", "breath" };
    private static readonly string[] SpearIdleClips = { "breath_Spear0", "breath" };
    private static readonly string[] PolearmIdleClips = { "breath_Polearm0", "breath" };
    private static readonly string[] BowIdleClips = { "breath_Bow0", "breath" };
    private static readonly string[] CrossbowIdleClips =
        { "breath_CrossBow0", "breath_CorossBow0", "breath_Bow0", "breath" };
    private static readonly string[] StaffIdleClips = { "breath_Bash0", "breath" };
    private static readonly string[] JamadarIdleClips = { "breathe_jamadar", "breath_Kick0", "breath" };
    private static readonly string[] UnarmedIdleClips = { "breath_Kick0", "breath", "basic" };

    private static void ForceDoubleSided(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
                if (mi.Mesh.SurfaceGetMaterial(i) is BaseMaterial3D bm)
                    bm.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        foreach (var c in n.GetChildren()) ForceDoubleSided(c);
    }

    private static T? FindFirst<T>(Node n) where T : class
    {
        if (n is T t) return t;
        foreach (var c in n.GetChildren())
        {
            var r = FindFirst<T>(c);
            if (r != null) return r;
        }
        return null;
    }

    internal static System.Collections.Generic.Dictionary<int, MeshInstance3D> BodyParts(Node body)
    {
        var parts = new System.Collections.Generic.Dictionary<int, MeshInstance3D>();
        void Walk(Node x)
        {
            foreach (var c in x.GetChildren())
            {
                if (c is BoneAttachment3D || c.Name == TransformNodeName) continue;
                if (c is MeshInstance3D mi && PartNodeIndex(mi.Name) is var idx && idx >= 0)
                    parts[idx] = mi;
                Walk(c);
            }
        }
        Walk(body);
        return parts;
    }

    internal static AnimationPlayer? BodyAnim(Node body)
    {
        foreach (var c in body.GetChildren())
        {
            if (c is BoneAttachment3D || c.Name == TransformNodeName) continue;
            if (c is AnimationPlayer ap) return ap;
            if (BodyAnim(c) is { } nested) return nested;
        }
        return null;
    }

    private static System.Collections.Generic.List<T> FindAll<T>(Node n) where T : class
    {
        var list = new System.Collections.Generic.List<T>();
        void Walk(Node x) { if (x is T t) list.Add(t); foreach (var c in x.GetChildren()) Walk(c); }
        Walk(n);
        return list;
    }

    private static Mesh? FindFirstMesh(Node? n)
    {
        if (n is MeshInstance3D m && m.Mesh != null) return m.Mesh;
        if (n != null)
            foreach (var child in n.GetChildren())
            {
                var r = FindFirstMesh(child);
                if (r != null) return r;
            }
        return null;
    }

    internal static string SafeModelName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char ch in name.ToLowerInvariant())
            sb.Append((char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.') ? ch : '_');
        return sb.ToString();
    }

    private static Color ColorFor(EntitySnapshot info)
    {
        if (info.IsNpc)
            return info.Attackable ? new Color(0.8f, 0.3f, 0.25f)
                                   : new Color(0.4f, 0.85f, 0.4f);
        return info.Nation == Nations.Karus ? new Color(0.85f, 0.4f, 0.4f)
                                            : new Color(0.45f, 0.6f, 1f);
    }

    private static Node3D MakeMapObjectEntity(string name)
    {
        var body = new Node3D();
        body.AddChild(NameLabel(name, 2.2f));
        return body;
    }

    private static MeshInstance3D MakeEntity(Color color, string name)
    {
        var body = new MeshInstance3D { Mesh = new CapsuleMesh() };
        body.MaterialOverride = new StandardMaterial3D { AlbedoColor = color };
        body.AddChild(NameLabel(name, 2.2f));
        return body;
    }
}

