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
    internal const float DefaultAlphaCutoff = 0.01f;

    private const string MetaKey = "gko_shine";

    private static readonly float[] PeakByLevel = { 0.45f, 0.60f, 0.78f, 1.0f };
    private static readonly bool[] PulsesByLevel = { true, true, true, false };
    private static readonly float[] BlinkFloorByLevel = { 0.55f, 0.30f, 0.02f, 0f };
    private static readonly float[] BlinkRateByLevel = { 0.62f, 0.85f, 1.25f, 1f };
    private static readonly float[] StrengthByLevel = { 0.7f, 1.25f, 2.2f, 3.6f };

    private static readonly int[] UpgradeLevelFloors = { 7, 8, 9, 10 };
    private static readonly int[] ReverseLevelFloors = { 1, 5, 11, 21 };

    public static int LevelFor(int itemId)
    {
        if (itemId <= 0) return 0;
        int plus = ItemData.UpgradeLevel(itemId);
        return ItemData.ExtFor(itemId) is { MagicOrRare: ItemData.Rarity.Reverse or ItemData.Rarity.ReverseUnique }
            ? LevelForReversePlus(plus)
            : LevelForPlus(plus);
    }

    public static int LevelForPlus(int plus) => LevelFrom(plus, UpgradeLevelFloors);

    public static int LevelForReversePlus(int plus) => LevelFrom(plus, ReverseLevelFloors);

    private static int LevelFrom(int plus, int[] floors)
    {
        int level = 0;
        while (level < floors.Length && plus >= floors[level]) level++;
        return level;
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
                         + $" plus={ItemData.UpgradeLevel(itemId)} -> level 0 (no shine)");
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
            var (mat, shine) = SharedShine(src, level, partIndex);
            driver.Surfaces.Add((i, mesh.GetSurfaceOverrideMaterial(i), mat, shine));
            mesh.SetSurfaceOverrideMaterial(i, mat);
            any = true;
        }
        if (!any) { driver.QueueFree(); return null; }

        mesh.SetMeta(MetaKey, true);
        mesh.AddChild(driver);
        return driver;
    }

    private static readonly Dictionary<(ulong Source, int Level, int Part), (StandardMaterial3D Surface, ShaderMaterial Shine)> _sharedShine = Shutdown.Track(new Dictionary<(ulong Source, int Level, int Part), (StandardMaterial3D Surface, ShaderMaterial Shine)>());

    private static (StandardMaterial3D Surface, ShaderMaterial Shine) SharedShine(StandardMaterial3D src, int level, int partIndex)
    {
        var key = (src.GetInstanceId(), level, partIndex);
        if (_sharedShine.TryGetValue(key, out var cached)) return cached;
        var mat = (StandardMaterial3D)src.Duplicate();
        var shine = new ShaderMaterial { Shader = ShineShader, NextPass = mat.NextPass };
        shine.SetShaderParameter("albedo_texture", src.AlbedoTexture);
        shine.SetShaderParameter("albedo_color", src.AlbedoColor);
        shine.SetShaderParameter("uv_scale", src.Uv1Scale);
        shine.SetShaderParameter("uv_offset", src.Uv1Offset);
        shine.SetShaderParameter("use_texture", src.AlbedoTexture != null);
        shine.SetShaderParameter("use_alpha", src.Transparency != BaseMaterial3D.TransparencyEnum.Disabled);
        shine.SetShaderParameter("alpha_cutoff", src.Transparency == BaseMaterial3D.TransparencyEnum.AlphaScissor
            ? src.AlphaScissorThreshold : DefaultAlphaCutoff);
        PackShine(level, partIndex, out var a, out var b);
        shine.SetShaderParameter("level_strength", a.X);
        shine.SetShaderParameter("peak", a.Y);
        shine.SetShaderParameter("blink_rate", a.Z);
        shine.SetShaderParameter("blink_floor", a.W);
        shine.SetShaderParameter("pulses", b.X > 0f);
        shine.SetShaderParameter("phase", b.Y);
        ApplyTiming(shine);
        mat.NextPass = shine;
        _sharedShine[key] = (mat, shine);
        return (mat, shine);
    }

    internal static void PackShine(int level, int partIndex, out Vector4 strength, out Vector4 timing)
    {
        int tier = Mathf.Clamp(level, 1, MaxLevel) - 1;
        strength = new Vector4(StrengthByLevel[tier], PeakByLevel[tier], BlinkRateByLevel[tier],
            Mathf.Max(FloorValue, BlinkFloorByLevel[tier]));
        timing = new Vector4(PulsesByLevel[tier] ? 1f : 0f, partIndex * PhaseStepSeconds, 0f, 0f);
    }

    internal static void ApplyTiming(ShaderMaterial shine)
    {
        shine.SetShaderParameter("pulse_top", PeakValue);
        shine.SetShaderParameter("rise_seconds", RiseSeconds);
        shine.SetShaderParameter("hold_seconds", HoldHighSeconds);
        shine.SetShaderParameter("fall_seconds", FallSeconds);
        shine.SetShaderParameter("cycle_seconds", CycleSeconds);
    }

    private static Shader? _shineShader;
    private static Shader ShineShader => _shineShader ??= GD.Load<Shader>("res://shaders/item_shine.gdshader");
}

public partial class ItemShineDriver : Node
{
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

    public override void _ExitTree()
    {
        if (GetParent() is MeshInstance3D mi) Restore(mi);
    }
}
