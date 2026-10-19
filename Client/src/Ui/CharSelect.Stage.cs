using Godot;
using LibreKO.Domain;

namespace LibreKO;

public partial class CharSelect
{
    private const string ElMoradStageStem = "elmorad_intro";
    private const string KarusStageStem = "karus_intro";

    private static readonly Vector3 StageEyeMap = new(144.235f, -3.562f, 161.578f);
    private static readonly Vector3 StageAimOffsetMap = new(-42.017f, 1.12f, -27.08f);
    private static readonly Vector3 StageStandMap = new(141.868f, -4.319f, 159.231f);

    private const float StageEyePullbackMap = 1.2f;

    private static Vector3 StageCameraMap =>
        StageEyeMap + (StageEyeMap - StageStandMap).Normalized() * StageEyePullbackMap;

    private const float StageFov = 45f;
    private const float StageNear = 0.1f;
    private const float StageFar = 500f;
    private const float StageFillLightRange = 6f;
    private const float StageSceneryRadius = 190f;

    private const float CreateCameraDistance = 4.6f;
    private const float CreateCameraHeight = 1.25f;
    private const float CreateCameraAimHeight = 1.05f;
    private const float CreateCameraFov = 34f;
    private const float CreateCameraHeadHeight = 1.6f;
    private const float CreateZoomMin = 0.35f;
    private const float CreateZoomStep = 0.1f;
    private float createZoom = 1f;

    private Terrain? stageTerrain;
    private Sky? stageSky;
    private Camera3D stageCamera = null!;
    private FxLayer? stageFxLayer;
    private float stageStandYaw;
    private Vector3 stageFacing = Vector3.Back;

    private static string StageStem(int nation) =>
        nation == Nations.Karus ? KarusStageStem : ElMoradStageStem;

    internal static int PreviewNation = Nations.Unknown;

    private int StageNation()
    {
        if (PreviewNation is Nations.Karus or Nations.ElMorad)
            return PreviewNation;
        if (Game.Session.Self.Nation is Nations.Karus or Nations.ElMorad)
            return Game.Session.Self.Nation;
        foreach (var c in characters)
            return NationOf(c.Race);
        return Nations.ElMorad;
    }

    private Vector3 StageToWorld(Vector3 ko) =>
        stageTerrain != null ? stageTerrain.MapToWorld(ko.X, ko.Y, ko.Z)
                              : Coord.ToGodot(ko.X, ko.Y, ko.Z);

    private void BuildStage()
    {
        stageSky = new Sky { Name = "Sky" };
        AddChild(stageSky);

        var terrain = new Terrain { Name = "Terrain", RenderDistance = StageFar };
        AddChild(terrain);
        if (terrain.BuildStem(StageStem(StageNation())))
        {
            stageTerrain = terrain;
            if (SceneryBuilder.Build(terrain, StageStandMap, StageSceneryRadius) is { } scenery)
                AddChild(scenery);
        }
        else
        {
            terrain.QueueFree();
            BuildFallbackGround();
        }

        Vector3 eye = StageToWorld(StageCameraMap);
        Vector3 aim = StageToWorld(StageCameraMap + StageAimOffsetMap);
        Vector3 stand = StageToWorld(StageStandMap);

        stageCamera = new Camera3D
        {
            Name = "SelectionCamera",
            Current = true,
            Fov = StageFov,
            Near = StageNear,
            Far = StageFar,
        };
        AddChild(stageCamera);
        stageCamera.LookAtFromPosition(eye, aim, Vector3.Up);
        stageFxLayer = FxLayer.Attach(GetViewport());

        stageStandYaw = -Coord.MapHeading(StageEyeMap.X - StageStandMap.X, StageEyeMap.Z - StageStandMap.Z);

        characterAnchor = new Node3D
        {
            Name = "SelectedCharacter",
            Position = stand,
            RotationDegrees = new Vector3(0, stageStandYaw, 0),
        };
        AddChild(characterAnchor);

        var toCamera = (eye - stand) with { Y = 0f };
        toCamera = toCamera.LengthSquared() > 0.0001f ? toCamera.Normalized() : Vector3.Back;
        stageFacing = toCamera;
        AddChild(new OmniLight3D
        {
            Name = "StandFill",
            Position = stand + toCamera * 2.2f + new Vector3(0, 2.6f, 0),
            OmniRange = StageFillLightRange,
            LightEnergy = 0.7f,
            LightColor = new Color(1f, 0.97f, 0.9f),
        });

        stageSky.Tick(StageDayFraction, Weather.Sunny, eye);
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
        stageSky?.Tick(StageDayFraction, Weather.Sunny, stageCamera.GlobalPosition);
    }

    private void ZoomCreateCamera(int steps)
    {
        createZoom = Mathf.Clamp(createZoom + steps * CreateZoomStep, CreateZoomMin, 1f);
        FrameStageCamera(forCreate: true);
    }

    private void FrameStageCamera(bool forCreate)
    {
        if (stageCamera == null) return;
        if (forCreate)
        {
            Vector3 stand = characterAnchor.Position;
            float closeness = (1f - createZoom) / (1f - CreateZoomMin);
            float eyeY = Mathf.Lerp(CreateCameraHeight, CreateCameraHeadHeight, closeness);
            float aimY = Mathf.Lerp(CreateCameraAimHeight, CreateCameraHeadHeight, closeness);
            stageCamera.Fov = CreateCameraFov;
            stageCamera.LookAtFromPosition(
                stand + stageFacing * CreateCameraDistance * createZoom + new Vector3(0, eyeY, 0),
                stand + new Vector3(0, aimY, 0),
                Vector3.Up);
            return;
        }
        stageCamera.Fov = StageFov;
        stageCamera.LookAtFromPosition(
            StageToWorld(StageCameraMap), StageToWorld(StageCameraMap + StageAimOffsetMap), Vector3.Up);
    }
}
