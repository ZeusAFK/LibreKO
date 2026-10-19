using Godot;

namespace LibreKO.Features.Windows;

internal sealed class AwakenFeature : IFeature
{
    private readonly IWorldContext context;
    private readonly AwakenSession awaken;

    public AwakenFeature(IWorldContext context, AwakenSession awaken)
    {
        this.context = context;
        this.awaken = awaken;
    }

    private const double FxLife = 2.0;
    private const float FxBaseScale = 1.6f;
    private const float FxHeight = 1.0f;

    public void Build()
    {
        awaken.AwakenEvent += On;
    }

    public void Dispose()
    {
        awaken.AwakenEvent -= On;
    }

    private void On(float effectScale, int effectId)
    {
        if (!context.Ready || context.Self.Body == null) return;

        context.Log.Notice("Awakening succeeded — your power surges.");
        SpawnBurst(effectScale);
    }

    private void SpawnBurst(float scale)
    {
        float s = Mathf.Clamp(scale <= 0f ? 1f : scale, 0.5f, 4f) * FxBaseScale;

        var fx = new GpuParticles3D
        {
            Amount = 96,
            Lifetime = 1.1,
            OneShot = true,
            Explosiveness = 0.85f,
            Emitting = true,
            Scale = new Vector3(s, s, s),
            Position = new Vector3(0, FxHeight, 0),
            DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth,
        };

        var proc = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.35f,
            Direction = new Vector3(0, 1, 0),
            Spread = 35f,
            Gravity = new Vector3(0, 2.2f, 0),
            InitialVelocityMin = 1.4f,
            InitialVelocityMax = 3.2f,
            ScaleMin = 0.12f,
            ScaleMax = 0.30f,
            Color = new Color(1.0f, 0.85f, 0.35f),
        };
        fx.ProcessMaterial = proc;

        fx.DrawPass1 = new QuadMesh { Size = new Vector2(0.5f, 0.5f) };
        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            DisableFog = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(1.0f, 0.9f, 0.5f),
        };
        fx.MaterialOverride = mat;

        context.Self.Body.AddChild(fx);

        var tween = context.Root.CreateTween();
        tween.TweenInterval(FxLife * 0.5);
        tween.TweenProperty(mat, "albedo_color:a", 0.0f, FxLife * 0.5);

        fx.After(FxLife, fx.QueueFree);
    }
}
