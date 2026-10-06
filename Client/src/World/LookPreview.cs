using Godot;

namespace LibreKO;

public partial class LookPreview : SubViewportContainer
{
    private const float CameraDistance = 3.4f;
    private const float CameraHeight = 1.15f;
    private const float CameraAim = 0.95f;
    private const float CameraFov = 34f;

    private readonly Node3D _pivot = new();
    private Node3D? _model;

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
        var eye = new Vector3(0f, CameraHeight, CameraDistance);
        var aim = new Vector3(0f, CameraAim, 0f);
        viewport.AddChild(new Camera3D
        {
            Fov = CameraFov,
            Current = true,
            Transform = new Transform3D(Basis.LookingAt(aim - eye, Vector3.Up), eye),
        });
        viewport.AddChild(_pivot);
    }

    public void Show(int race, int face, int hairStyle, Color hairColour)
    {
        Clear();
        _model = CharacterPreview.Build(race, face, System.Array.Empty<int>(), hairStyle, hairColour);
        if (_model != null)
            _pivot.AddChild(_model);
    }

    public void Turn(float degrees) => _pivot.RotateY(Mathf.DegToRad(degrees));

    public void Clear()
    {
        _model?.QueueFree();
        _model = null;
    }
}
