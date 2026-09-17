using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class CharSelect
{
    private const string ElMoradStageStem = "elmorad_intro";
    private const string KarusStageStem = "karus_intro";

    private static readonly Vector3 StageEyeKo = new(144.235f, -3.562f, 161.578f);
    private static readonly Vector3 StageAimOffsetKo = new(-42.017f, 1.12f, -27.08f);
    private static readonly Vector3 StageStandKo = new(141.868f, -4.319f, 159.231f);

    private const float StageEyePullbackKo = 1.2f;

    private static Vector3 StageCameraKo =>
        StageEyeKo + (StageEyeKo - StageStandKo).Normalized() * StageEyePullbackKo;

    private const float StageFov = 45f;
    private const float StageNear = 0.1f;
    private const float StageFar = 500f;
    private const float StageFillLightRange = 6f;
    private const float StageSceneryRadius = 190f;

    private const float CreateCameraDistance = 4.6f;
    private const float CreateCameraHeight = 1.25f;
    private const float CreateCameraAimHeight = 1.05f;
    private const float CreateCameraFov = 34f;

    private Terrain? _stageTerrain;
    private Sky? _stageSky;
    private Camera3D _stageCamera = null!;
    private float _stageStandYaw;
    private Vector3 _stageFacing = Vector3.Back;

    private static string StageStem(int nation) =>
        nation == Nations.Karus ? KarusStageStem : ElMoradStageStem;

    internal static int PreviewNation = Nations.Unknown;

    private int StageNation()
    {
        if (PreviewNation is Nations.Karus or Nations.ElMorad)
            return PreviewNation;
        if (Net.I.Nation is Nations.Karus or Nations.ElMorad)
            return Net.I.Nation;
        foreach (var c in _characters)
            return NationOf(c.Race);
        return Nations.ElMorad;
    }

    private Vector3 StageToWorld(Vector3 ko) =>
        _stageTerrain != null ? _stageTerrain.KoToWorld(ko.X, ko.Y, ko.Z)
                              : Coord.ToGodot(ko.X, ko.Y, ko.Z);

    private void BuildStage()
    {
        _stageSky = new Sky { Name = "Sky" };
        AddChild(_stageSky);

        var terrain = new Terrain { Name = "Terrain", RenderDistance = StageFar };
        AddChild(terrain);
        if (terrain.BuildStem(StageStem(StageNation())))
        {
            _stageTerrain = terrain;
            if (KoScenery.Build(terrain, StageStandKo, StageSceneryRadius) is { } scenery)
                AddChild(scenery);
        }
        else
        {
            terrain.QueueFree();
            BuildFallbackGround();
        }

        Vector3 eye = StageToWorld(StageCameraKo);
        Vector3 aim = StageToWorld(StageCameraKo + StageAimOffsetKo);
        Vector3 stand = StageToWorld(StageStandKo);

        _stageCamera = new Camera3D
        {
            Name = "SelectionCamera",
            Current = true,
            Fov = StageFov,
            Near = StageNear,
            Far = StageFar,
        };
        AddChild(_stageCamera);
        _stageCamera.LookAtFromPosition(eye, aim, Vector3.Up);

        _stageStandYaw = -Coord.KoHeading(StageEyeKo.X - StageStandKo.X, StageEyeKo.Z - StageStandKo.Z);

        _characterAnchor = new Node3D
        {
            Name = "SelectedCharacter",
            Position = stand,
            RotationDegrees = new Vector3(0, _stageStandYaw, 0),
        };
        AddChild(_characterAnchor);

        var toCamera = (eye - stand) with { Y = 0f };
        toCamera = toCamera.LengthSquared() > 0.0001f ? toCamera.Normalized() : Vector3.Back;
        _stageFacing = toCamera;
        AddChild(new OmniLight3D
        {
            Name = "StandFill",
            Position = stand + toCamera * 2.2f + new Vector3(0, 2.6f, 0),
            OmniRange = StageFillLightRange,
            LightEnergy = 0.7f,
            LightColor = new Color(1f, 0.97f, 0.9f),
        });

        _stageSky.Tick(StageDayFraction, Weather.Sunny, eye);
    }

    private const float StageDayFraction = 0.42f;

    private void BuildFallbackGround()
    {
        AddChild(new MeshInstance3D
        {
            Name = "SelectionGround",
            Mesh = new PlaneMesh { Size = new Vector2(120, 120) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.20f, 0.28f, 0.14f),
                Roughness = 0.94f,
            },
        });
    }

    private void TickStage(double delta)
    {
        _stageSky?.Tick(StageDayFraction, Weather.Sunny, _stageCamera.GlobalPosition);
    }

    private void FrameStageCamera(bool forCreate)
    {
        if (_stageCamera == null) return;
        if (forCreate)
        {
            Vector3 stand = _characterAnchor.Position;
            _stageCamera.Fov = CreateCameraFov;
            _stageCamera.LookAtFromPosition(
                stand + _stageFacing * CreateCameraDistance + new Vector3(0, CreateCameraHeight, 0),
                stand + new Vector3(0, CreateCameraAimHeight, 0),
                Vector3.Up);
            return;
        }
        _stageCamera.Fov = StageFov;
        _stageCamera.LookAtFromPosition(
            StageToWorld(StageCameraKo), StageToWorld(StageCameraKo + StageAimOffsetKo), Vector3.Up);
    }
}
