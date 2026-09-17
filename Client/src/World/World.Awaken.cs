using Godot;
using LibreKO.Network;

namespace LibreKO;

public partial class World
{
    private const double AwakenFxLife = 2.0;
    private const float AwakenFxBaseScale = 1.6f;
    private const float AwakenFxHeight = 1.0f;

    private void AwakenInit()
    {
        Net.I.AwakenEvent += OnAwaken;
    }

    private void AwakenDispose()
    {
        Net.I.AwakenEvent -= OnAwaken;
    }

    private void OnAwaken(float effectScale, int effectId)
    {
        if (!_worldReady || _self == null) return;

        Chat.Info("Awakening succeeded — your power surges.");
        SpawnAwakenBurst(effectScale);
    }

    private void SpawnAwakenBurst(float scale)
    {
        float s = Mathf.Clamp(scale <= 0f ? 1f : scale, 0.5f, 4f) * AwakenFxBaseScale;

        var fx = new GpuParticles3D
        {
            Amount = 96,
            Lifetime = 1.1,
            OneShot = true,
            Explosiveness = 0.85f,
            Emitting = true,
            Scale = new Vector3(s, s, s),
            Position = new Vector3(0, AwakenFxHeight, 0),
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

        _self.AddChild(fx);

        var tween = CreateTween();
        tween.TweenInterval(AwakenFxLife * 0.5);
        tween.TweenProperty(mat, "albedo_color:a", 0.0f, AwakenFxLife * 0.5);

        var timer = GetTree().CreateTimer(AwakenFxLife);
        timer.Timeout += () => { if (GodotObject.IsInstanceValid(fx)) fx.QueueFree(); };
    }
}
