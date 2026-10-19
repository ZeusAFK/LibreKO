using System.Collections.Generic;
using Godot;

namespace LibreKO;

internal sealed class FxParticleTemplate
{
    public Material Process = null!;
    public ShaderMaterial Material = null!;
    public FxEmitterKeys? Emitter;
    public int Capacity;
    public int NumCreate;
    public float EmitInterval;
    public float Lifetime;
    public float LifeMax;
    public float Speed;
    public float Spread;
    public bool Gather;
    public bool SingleBurst;
    public bool Commutative;
    public Vector3 EmitAxis;
    public Vector3 BoxOffset;
    public Vector3 BoxExtent;
    public Vector3 GatherPoint;
    public float OrbitRate;
    public float Start, Life, FadeIn, FadeOut, HideTime, ShowTime;
    public Vector3 Origin, Velocity, Acceleration;
}

internal sealed class FxEmitterKeys
{
    public Vector3 Centre;
    public Vector3[]? Pos, Scale;
    public Quaternion[]? Rot;
    public float Fps, PosRate, RotRate, ScaleRate, WholeFrame;
}

public partial class FxSharedEmitter : GpuParticles3D
{
    internal int Slots;
    internal ulong ViewportId;
    internal FxPartKey Part;

    public override void _ExitTree() => FxEmitterPool.Forget(this);
}

internal static class FxEmitterPool
{
    private const int InstancesPerEmitter = 64;
    private const float SharedAabbHalf = 4096f;

    private static readonly ViewportPool<FxPartKey, FxSharedEmitter> emitters = new();
    private static QuadMesh? quad;

    internal static int Live => emitters.Live;

    internal static FxSharedEmitter? Acquire(Node part, FxPartKey key, FxParticleTemplate template)
    {
        var viewport = part.GetViewport();
        if (viewport == null) return null;
        ulong viewportId = viewport.GetInstanceId();
        var shelf = emitters.Shelf(viewportId, key);
        for (int i = shelf.Count - 1; i >= 0; i--)
        {
            var existing = shelf[i];
            if (!GodotObject.IsInstanceValid(existing)) { emitters.Forget(viewportId, key, existing); continue; }
            if (existing.Slots < InstancesPerEmitter) { existing.Slots++; return existing; }
        }
        var parent = FxBoardBatch.For(part);
        if (parent == null) return null;
        var emitter = new FxSharedEmitter
        {
            Name = $"emit_{key.Name}_{key.Index}",
            Amount = template.Capacity * InstancesPerEmitter,
            AmountRatio = 1f,
            Lifetime = template.Lifetime,
            Emitting = false,
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = FxShading.LayerBit,
            VisibilityAabb = new Aabb(-Vector3.One * SharedAabbHalf, Vector3.One * (2f * SharedAabbHalf)),
            DrawOrder = template.Commutative ? GpuParticles3D.DrawOrderEnum.Index : GpuParticles3D.DrawOrderEnum.ViewDepth,
            ProcessMaterial = template.Process,
            DrawPass1 = quad ??= new QuadMesh { Size = Vector2.One },
            MaterialOverride = template.Material,
            Slots = 1,
            ViewportId = viewportId,
            Part = key,
        };
        parent.AddChild(emitter);
        emitters.Add(viewportId, key, emitter);
        return emitter;
    }

    internal static void Release(FxSharedEmitter? emitter)
    {
        if (emitter != null && GodotObject.IsInstanceValid(emitter) && emitter.Slots > 0) emitter.Slots--;
    }

    internal static void Forget(FxSharedEmitter emitter) => emitters.Forget(emitter.ViewportId, emitter.Part, emitter);

    internal static void Clear()
    {
        foreach (var emitter in emitters.Items)
            if (GodotObject.IsInstanceValid(emitter)) emitter.QueueFree();
        emitters.Clear();
    }
}
