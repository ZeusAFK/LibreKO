using Godot;

namespace LibreKO.Presentation;

public partial class LookPreview : SubViewportContainer
{
    private const float CameraDistance = 3.4f;
    private const float CameraHeight = 1.15f;
    private const float CameraAim = 0.95f;
    private const float CameraFov = 34f;
    private const float ZoomDistance = 0.8f;
    private const float HeadAimShare = 0.93f;

    private readonly Node3D pivot = new();
    private readonly Camera3D camera;
    private readonly SubViewport viewport;
    private Node3D? model;
    private bool zoomed;

    public LookPreview(int width, int height)
    {
        Stretch = true;
        CustomMinimumSize = new Vector2(width, height);
        var viewport = new SubViewport
        {
            OwnWorld3D = true,
            TransparentBg = true,
            Size = new Vector2I(width, height),
            RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
        };
        this.viewport = viewport;
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.85f, 0.83f, 0.8f),
                AmbientLightEnergy = 0.7f,
            },
        });
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35f, 25f, 0f), LightEnergy = 1.1f });
        camera = new Camera3D { Fov = CameraFov, Current = true };
        viewport.AddChild(camera);
        viewport.AddChild(pivot);
        PlaceCamera();
    }

    public Texture2D Texture => viewport.GetTexture();

    public bool AlwaysRender
    {
        set => viewport.RenderTargetUpdateMode = value ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.WhenVisible;
    }

    public void ToggleZoom()
    {
        zoomed = !zoomed;
        PlaceCamera();
    }

    private void PlaceCamera()
    {
        float aimY = CameraAim, eyeY = CameraHeight, distance = CameraDistance;
        if (zoomed && ModelTop() is float top)
        {
            aimY = eyeY = top * HeadAimShare;
            distance = ZoomDistance;
        }
        var eye = new Vector3(0f, eyeY, distance);
        var aim = new Vector3(0f, aimY, 0f);
        camera.Transform = new Transform3D(Basis.LookingAt(aim - eye, Vector3.Up), eye);
    }

    private float? ModelTop()
    {
        if (model == null) return null;
        float? top = null;
        foreach (var node in model.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (node is not MeshInstance3D mesh || mesh.Mesh == null) continue;
            var box = mesh.GlobalTransform * mesh.GetAabb();
            float end = box.End.Y;
            if (top == null || end > top) top = end;
        }
        return top;
    }

    public void Show(int race, int face, int hairStyle, Color hairColour)
    {
        Clear();
        model = CharacterPreview.Build(race, face, System.Array.Empty<int>(), hairStyle, hairColour);
        if (model != null)
            pivot.AddChild(model);
        if (zoomed) PlaceCamera();
    }

    public void Turn(float degrees) => pivot.RotateY(Mathf.DegToRad(degrees));

    public void Clear()
    {
        model?.QueueFree();
        model = null;
    }
}
