using Godot;

namespace LibreKO;

public static class ItemShine
{
    public const int MaxLevel = 4;
    public static bool Verbose = false;

    private const float RiseSeconds = 1.4f;
    private const float HoldHighSeconds = 2.1f;
    private const float FallSeconds = 1.75f;
    private const float HoldLowSeconds = 0.35f;
    private const float CycleSeconds = RiseSeconds + HoldHighSeconds + FallSeconds + HoldLowSeconds;
    private const float FloorValue = 0.0055f;
    private const float PeakValue = 1.0f;
    private const float PhaseStepSeconds = 0.6f;

    private const string MetaKey = "gko_shine";

    private static readonly float[] PeakByLevel = { 0.45f, 0.60f, 0.78f, 1.0f };
    private static readonly bool[] PulsesByLevel = { true, true, true, false };
    private static readonly float[] BlinkFloorByLevel = { 0.55f, 0.30f, 0.02f, 0f };
    private static readonly float[] BlinkRateByLevel = { 0.62f, 0.85f, 1.25f, 1f };

    private static System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, int>>? _cats;

    private static void Load()
    {
        _cats = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.Dictionary<int, int>>();

        const string path = "res://assets/items/shine.json";
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (f == null)
        {
            GD.PushWarning($"[itemshine] cannot open {path} (err {Godot.FileAccess.GetOpenError()})"
                           + " - run: python tools/bake_item_shine.py");
            return;
        }
        if (Json.ParseString(f.GetAsText()).AsGodotDictionary() is not { } root
            || !root.TryGetValue("cats", out var cs) || cs.AsGodotDictionary() is not { } csd)
        {
            GD.PushWarning($"[itemshine] {path} is not a valid shine table");
            return;
        }

        foreach (var key in csd.Keys)
        {
            if (!int.TryParse(key.AsString(), out int cat) || csd[key].AsGodotDictionary() is not { } extd)
                continue;
            var map = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var e in extd.Keys)
                if (int.TryParse(e.AsString(), out int ext))
                    map[ext] = extd[e].AsInt32();
            _cats[cat] = map;
        }
    }

    public static int LevelFor(int itemId)
    {
        if (itemId <= 0) return 0;
        if (_cats == null) Load();
        if (_cats == null || _cats.Count == 0) return 0;
        int baseId = itemId / 1000 * 1000;
        if (ItemData.Get(baseId) is not { } item) return 0;
        return _cats.TryGetValue(item.Cat, out var map) && map.TryGetValue(itemId - baseId, out int level)
            ? level
            : 0;
    }

    public static float Envelope(int level, double now, int partIndex)
    {
        if (level <= 0) return 0f;
        int i = Mathf.Clamp(level, 1, MaxLevel) - 1;
        float peak = PeakByLevel[i];
        if (!PulsesByLevel[i]) return peak;

        float rate = BlinkRateByLevel[i];
        float floor = Mathf.Max(FloorValue, BlinkFloorByLevel[i]);
        float cycle = CycleSeconds / rate;
        float rise = RiseSeconds / rate, hold = HoldHighSeconds / rate, fall = FallSeconds / rate;

        double t = Mathf.PosMod((float)now + partIndex * PhaseStepSeconds, cycle);
        float v;
        if (t < rise)
            v = Mathf.Lerp(floor, PeakValue, (float)(t / rise));
        else if (t < rise + hold)
            v = PeakValue;
        else if (t < rise + hold + fall)
            v = Mathf.Lerp(PeakValue, floor, (float)((t - rise - hold) / fall));
        else
            v = floor;
        return v * peak;
    }

    internal static float PeakFor(int level) => PeakByLevel[Mathf.Clamp(level, 1, MaxLevel) - 1];

    public static void Apply(Node? root, int itemId, int partIndex)
    {
        if (root == null)
        {
            if (Verbose) GD.Print($"[itemshine] item={itemId} part={partIndex}: NO NODE");
            return;
        }
        int meshes = 0, shined = 0;
        foreach (var mesh in Meshes(root))
        {
            meshes++;
            if (ApplyTo(mesh, itemId, partIndex)) shined++;
        }
        if (Verbose && meshes != 1)
            GD.Print($"[itemshine] item={itemId} part={partIndex}: {shined}/{meshes} meshes shined");
    }

    private static System.Collections.Generic.IEnumerable<MeshInstance3D> Meshes(Node n)
    {
        if (n is FxInstance or FxWeaponGlow) yield break;
        if (n is MeshInstance3D mi && mi.Mesh != null) yield return mi;
        foreach (var c in n.GetChildren())
            foreach (var m in Meshes(c))
                yield return m;
    }

    private static bool ApplyTo(MeshInstance3D mesh, int itemId, int partIndex)
    {
        int level = LevelFor(itemId);
        if (level <= 0)
        {
            if (Verbose)
            {
                int baseId = itemId / 1000 * 1000;
                int cat = ItemData.Get(baseId) is { } it ? it.Cat : -1;
                GD.Print($"[itemshine] item={itemId} base={baseId} ext={itemId - baseId} cat={cat}"
                         + $" tableCats={(_cats?.Count ?? -1)} -> level 0 (no shine)");
            }
            if (mesh.GetMeta(MetaKey, false).AsBool()) Clear(mesh);
            return false;
        }
        if (Prepare(mesh, level, partIndex) is not { } shine)
        {
            if (Verbose)
                GD.Print($"[itemshine] item={itemId} level={level} but NO StandardMaterial3D on"
                         + $" '{mesh.Name}' (surfaces={mesh.Mesh?.GetSurfaceCount() ?? -1})");
            return false;
        }
        shine.Level = level;
        shine.PartIndex = partIndex;
        if (Verbose) GD.Print($"[itemshine] item={itemId} level={level} part={partIndex} ACTIVE on '{mesh.Name}'");
        return true;
    }

    public static void Clear(MeshInstance3D mesh)
    {
        foreach (var child in mesh.GetChildren())
            if (child is ItemShineDriver d)
            {
                d.Restore(mesh);
                mesh.RemoveChild(d);
                d.QueueFree();
            }
        if (mesh.HasMeta(MetaKey)) mesh.RemoveMeta(MetaKey);
    }

    private static ItemShineDriver? Prepare(MeshInstance3D mesh, int level, int partIndex)
    {
        foreach (var child in mesh.GetChildren())
            if (child is ItemShineDriver existing)
            {
                if (existing.SourceMesh == mesh.Mesh) return existing;
                Clear(mesh);
                break;
            }

        if (mesh.Mesh == null) return null;
        var driver = new ItemShineDriver { Name = "shine", SourceMesh = mesh.Mesh, Level = level, PartIndex = partIndex };
        bool any = false;
        for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
        {
            if (mesh.GetActiveMaterial(i) is not StandardMaterial3D src)
            {
                continue;
            }
            var mat = (StandardMaterial3D)src.Duplicate();
            var shine = new ShaderMaterial { Shader = ShineShader, NextPass = mat.NextPass };
            shine.SetShaderParameter("albedo_texture", src.AlbedoTexture);
            shine.SetShaderParameter("albedo_color", src.AlbedoColor);
            shine.SetShaderParameter("uv_scale", src.Uv1Scale);
            shine.SetShaderParameter("uv_offset", src.Uv1Offset);
            shine.SetShaderParameter("use_texture", src.AlbedoTexture != null);
            shine.SetShaderParameter("use_alpha", src.Transparency != BaseMaterial3D.TransparencyEnum.Disabled);
            shine.SetShaderParameter("alpha_cutoff", src.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor
                ? src.AlphaScissorThreshold : 0.01f);
            mat.NextPass = shine;
            driver.Surfaces.Add((i, mesh.GetSurfaceOverrideMaterial(i), mat, shine));
            mesh.SetSurfaceOverrideMaterial(i, mat);
            any = true;
        }
        if (!any) { driver.QueueFree(); return null; }

        mesh.SetMeta(MetaKey, true);
        mesh.AddChild(driver);
        return driver;
    }

    private static Shader? _shineShader;
    private static Shader ShineShader => _shineShader ??= GD.Load<Shader>("res://shaders/item_shine.gdshader");
}

public partial class ItemShineDriver : Node
{
    private static readonly StringName StrengthParameter = new("strength");
    private float _lastStrength = float.NaN;
    public readonly System.Collections.Generic.List<(int Index, Material? Original, StandardMaterial3D Surface, ShaderMaterial Shine)> Surfaces = new();
    public Mesh? SourceMesh;
    public int Level;
    public int PartIndex;

    public void Restore(MeshInstance3D mesh)
    {
        foreach (var s in Surfaces)
            if (s.Index < mesh.GetSurfaceOverrideMaterialCount() && mesh.GetSurfaceOverrideMaterial(s.Index) == s.Surface)
                mesh.SetSurfaceOverrideMaterial(s.Index, s.Original);
        Surfaces.Clear();
    }

    public override void _Process(double delta)
    {
        if (Fx.ShuttingDown || GetParent() is not MeshInstance3D mesh || !mesh.IsVisibleInTree()) return;
        float v = ItemShine.Envelope(Level, Time.GetTicksMsec() * 0.001, PartIndex);
        float strength = Level switch { 1 => 0.7f, 2 => 1.25f, 3 => 2.2f, _ => 3.6f };
        strength *= v * (1f - mesh.Transparency);
        if (strength == _lastStrength) return;
        _lastStrength = strength;
        foreach (var s in Surfaces)
        {
            s.Shine.SetShaderParameter(StrengthParameter, strength);
        }
    }

    public override void _ExitTree()
    {
        if (GetParent() is MeshInstance3D mi) Restore(mi);
    }
}
