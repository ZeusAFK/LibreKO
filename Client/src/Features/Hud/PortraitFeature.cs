using Godot;

namespace LibreKO.Features.Hud;

internal sealed class PortraitFeature : IFeature, ITickFeature
{
    private readonly IWorldContext context;

    public PortraitFeature(IWorldContext context)
    {
        this.context = context;
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    public void Tick(double delta, double nowSec)
    {
        PortraitTick(nowSec);
    }

    private const uint Layer = 1u << 19;
    private const int Pixels = 384;
    private const float Distance = 3.4f;
    private const float EyeHeight = 1.05f;
    private const float Fov = 32f;
    private const float Ambient = 0.55f;
    private const double RemarkSeconds = 0.5;

    private SubViewport? viewport;

    private Camera3D? camera;
    private int viewers;
    private double remarkAt;

    internal Texture2D? Texture
    {
        get
        {
            Ensure();
            return viewport?.GetTexture();
        }
    }

    internal void SetShown(bool shown)
    {
        viewers = Mathf.Max(0, viewers + (shown ? 1 : -1));
        Ensure();
        if (viewport == null) return;
        viewport.RenderTargetUpdateMode = viewers > 0
            ? SubViewport.UpdateMode.Always
            : SubViewport.UpdateMode.Disabled;
        remarkAt = 0;
    }

    private void Ensure()
    {
        if (viewport != null || !context.Root.IsInsideTree()) return;
        viewport = new SubViewport
        {
            Size = new Vector2I(Pixels, Pixels),
            TransparentBg = true,
            World3D = context.Root.GetViewport().World3D,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        camera = new Camera3D
        {
            CullMask = Layer,
            Fov = Fov,
            Current = true,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = Ambient,
            },
        };
        viewport.AddChild(camera);
        context.Root.AddChild(viewport);
    }

    private void PortraitTick(double now)
    {
        if (viewers <= 0 || camera == null || context.Self.Body == null) return;
        if (now >= remarkAt)
        {
            remarkAt = now + RemarkSeconds;
            MarkLayer(context.Self.Body);
        }
        var front = new Vector3(context.Self.Facing.X, 0f, context.Self.Facing.Z);
        if (front.LengthSquared() < 0.01f) front = Vector3.Forward;
        var eye = context.Self.Body.GlobalPosition + Vector3.Up * (EyeHeight - context.Self.Lift);
        camera.GlobalPosition = eye + front.Normalized() * Distance;
        camera.LookAt(eye, Vector3.Up);
    }

    private static void MarkLayer(Node node)
    {
        if (node is VisualInstance3D visual) visual.Layers |= Layer;
        foreach (var child in node.GetChildren()) MarkLayer(child);
    }
}
