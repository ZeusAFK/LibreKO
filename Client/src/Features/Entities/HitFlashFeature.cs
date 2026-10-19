using Godot;

namespace LibreKO.Features.Entities;

internal sealed partial class HitFlashFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly EntitySystem entities;

    public HitFlashFeature(IWorldContext context, EntitySystem entities)
    {
        this.context = context;
        this.entities = entities;
    }

    public void Build() => entities.EntityStruck += Flash;

    public void Dispose() => entities.EntityStruck -= Flash;

    public void LateTick(double delta, double nowSec)
    {
        HitFlashTick(nowSec);
    }

    private const double Seconds = 0.3;
    internal static readonly Color Colour = new(1f, 0.2f, 0.2f);
    internal static readonly StringName Tint = "tint";

    internal static Shader FlashShader => Shaders.Get("hit_flash");

    private sealed class HitFlashState
    {
        public double Start;
        public ShaderMaterial Material = null!;
        public readonly List<GeometryInstance3D> Parts = new();
    }

    private readonly Dictionary<int, HitFlashState> flashes = new();
    private readonly List<int> done = new();

    private void Flash(int id)
    {
        if (!context.Entities.Views.TryGetValue(id, out var e) || !GodotObject.IsInstanceValid(e.Body)) return;
        if (flashes.TryGetValue(id, out var running))
        {
            running.Start = context.Now;
            return;
        }
        var state = new HitFlashState
        {
            Start = context.Now,
            Material = new ShaderMaterial { Shader = FlashShader },
        };
        state.Material.SetShaderParameter(Tint, Colour);
        foreach (var node in e.Body.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.MaterialOverlay != null || CutOut(mesh)) continue;
            mesh.MaterialOverlay = state.Material;
            state.Parts.Add(mesh);
        }
        if (state.Parts.Count > 0) flashes[id] = state;
    }

    internal static bool CutOut(MeshInstance3D mesh)
    {
        if (mesh.Mesh == null) return true;
        for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            if ((mesh.GetActiveMaterial(i) as BaseMaterial3D)?.Transparency is { } mode && mode != BaseMaterial3D.TransparencyEnum.Disabled)
                return true;
        return false;
    }

    private void HitFlashTick(double now)
    {
        if (flashes.Count == 0) return;
        foreach (var (id, state) in flashes)
        {
            float t = (float)((now - state.Start) / Seconds);
            if (t < 1f)
            {
                state.Material.SetShaderParameter(Tint, Colour.Lerp(Colors.White, t));
                continue;
            }
            foreach (var part in state.Parts)
                if (GodotObject.IsInstanceValid(part) && part.MaterialOverlay == state.Material) part.MaterialOverlay = null;
            done.Add(id);
        }
        foreach (int id in done) flashes.Remove(id);
        done.Clear();
    }
}
