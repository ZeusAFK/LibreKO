using Godot;

namespace LibreKO.Features.Core;

internal sealed class CameraSystem : IFeature
{
    private readonly IWorldContext context;
    private readonly SelfAvatar avatar;
    private readonly ScenerySystem scenery;

    public CameraSystem(IWorldContext context, SelfAvatar avatar, ScenerySystem scenery)
    {
        this.context = context;
        this.avatar = avatar;
        this.scenery = scenery;
    }

    private readonly CameraShake cameraShake = new();

    internal void ShakeCamera(float seconds, float amplitude, float rate)
    {
        if (Config.CameraShake) cameraShake.Start(seconds, amplitude, rate);
    }

    public void Build()
    {
    }

    public void Dispose()
    {
    }

    internal const float MinDist = 1.5f;
    internal const float MaxDist = 8.0f;
    internal const float InfoMaxDist = 100.0f;

    internal const float OrbitSens = 0.006f;
    private const float PadLookSpeed = 2.6f;
    internal const float MinPitch = 0.20f;
    internal const float MaxPitch = 1.45f;

    internal Camera3D Camera { get; private set; } = null!;

    internal float CamYaw { get; private set; }
    private float camPitch = 0.5f;
    private float camDist = MaxDist;
    private float camZoomGoal = MaxDist;
    private const float CamZoomEase = 12f;
    private bool testCameraFrozen;
    private bool halfTurnActive;
    private const float ZoomInStep = 0.9f;
    private const float ZoomOutStep = 1.12f;

    internal void Attach(Camera3D camera) => Camera = camera;

    internal void Orbit(Vector2 relative)
    {
        halfTurnActive = false;
        CamYaw -= relative.X * OrbitSens;
        camPitch = Mathf.Clamp(camPitch + relative.Y * OrbitSens, MinPitch, MaxPitch);
    }

    internal void ZoomIn(float maxDist) => camZoomGoal = Mathf.Clamp(camZoomGoal * ZoomInStep, MinDist, maxDist);

    internal void ZoomOut(float maxDist) => camZoomGoal = Mathf.Clamp(camZoomGoal * ZoomOutStep, MinDist, maxDist);

    internal void LimitZoom(float maxDist) => camZoomGoal = Mathf.Min(camZoomGoal, maxDist);
    private float halfTurnElapsed;
    private float halfTurnStartYaw;
    private const float CamAimHeight = 1.85f;
    private const float CamMinAimHeight = 0.9f;
    private const float CamEdgeMargin = 2f;
    private const float CamNearPad = 0.7f;
    private const int CamTerrainSteps = 10;
    private const float CamReturnSpeed = 6f;
    private float camDistClear = MaxDist;

    private static float CameraHalfTurnDuration =>
        180f / Mathf.Max(1f, Config.CamTurnSpeed);

    internal void FreezeTestCamera(bool frozen) => testCameraFrozen = frozen;

    internal void StartCameraHalfTurn()
    {
        if (halfTurnActive)
            return;

        halfTurnActive = true;
        halfTurnElapsed = 0f;
        halfTurnStartYaw = CamYaw;
    }

    private void CameraEdgePanTick(double delta)
    {
        if (!Config.CamEdgePan) return;
        if (DisplayServer.WindowGetMode() is not (DisplayServer.WindowMode.Fullscreen
            or DisplayServer.WindowMode.ExclusiveFullscreen)) return;

        var viewport = context.Root.GetViewport();
        if (viewport == null || viewport.GuiGetHoveredControl() != null) return;

        float width = viewport.GetVisibleRect().Size.X;
        float x = viewport.GetMousePosition().X;
        float dir = x <= CamEdgeMargin ? -1f : x >= width - CamEdgeMargin ? 1f : 0f;
        if (dir == 0f) return;

        halfTurnActive = false;
        CamYaw -= dir * Mathf.DegToRad(Config.CamEdgePanSpeed) * (float)delta;
    }

    private void CameraPadLookTick(double delta)
    {
        var look = KeyBinds.LookStick();
        if (look == Vector2.Zero) return;
        halfTurnActive = false;
        CamYaw -= look.X * PadLookSpeed * (float)delta;
        camPitch = Mathf.Clamp(camPitch + look.Y * PadLookSpeed * (float)delta, MinPitch, MaxPitch);
    }

    internal void UpdateCamera(double delta)
    {
        if (testCameraFrozen) return;
        CameraEdgePanTick(delta);
        CameraPadLookTick(delta);

        if (halfTurnActive)
        {
            halfTurnElapsed += (float)delta;
            float t = Mathf.Clamp(halfTurnElapsed / CameraHalfTurnDuration, 0f, 1f);
            float eased = t * t * (3f - 2f * t);
            CamYaw = halfTurnStartYaw - Mathf.Pi * eased;
            if (t >= 1f)
            {
                CamYaw = halfTurnStartYaw - Mathf.Pi;
                halfTurnActive = false;
            }
        }

        camDist = Mathf.Lerp(camDist, camZoomGoal, 1f - Mathf.Exp(-CamZoomEase * (float)delta));
        float aimT = Mathf.Clamp((camDist - MinDist) / (MaxDist - MinDist), 0f, 1f);
        float aimHeight = Mathf.Lerp(CamMinAimHeight, CamAimHeight, aimT);
        var target = avatar.Body.Position + Vector3.Up * aimHeight;
        float hd = camDist * Mathf.Cos(camPitch);
        float h = camDist * Mathf.Sin(camPitch);
        var boom = new Vector3(hd * Mathf.Sin(CamYaw), h, hd * Mathf.Cos(CamYaw));
        var dir = boom.Normalized();
        float clear = Mathf.Min(TerrainClearDist(target, dir), ObstacleClearDist(target, dir, camDist));
        camDistClear = clear < camDistClear
            ? clear
            : Mathf.MoveToward(camDistClear, clear, CamReturnSpeed * (float)delta);
        float shake = cameraShake.Step((float)delta);
        Camera.Position = EyeAboveTerrain(target + dir * camDistClear) + Vector3.Up * shake;
        Camera.LookAt(target + Vector3.Up * (shake * CameraShake.LookFactor), Vector3.Up);
    }

    private float TerrainClearDist(Vector3 target, Vector3 dir)
    {
        if (scenery.Terrain == null) return camDist;
        for (int i = 1; i <= CamTerrainSteps; i++)
        {
            float t = camDist * i / CamTerrainSteps;
            var p = target + dir * t;
            if (scenery.GroundHeight(p) is not { } ground) continue;
            if (p.Y - ground >= CamNearPad) continue;
            return Mathf.Max(MinDist, camDist * (i - 1) / CamTerrainSteps);
        }
        return camDist;
    }

    private PhysicsRayQueryParameters3D? camRay;
    private bool camRayExcludesSelf;

    private float ObstacleClearDist(Vector3 target, Vector3 dir, float dist)
    {
        if (avatar.NoClip) return dist;
        var query = camRay ??= new PhysicsRayQueryParameters3D { CollisionMask = PlayerSystem.WorldCollisionLayer };
        query.From = target;
        query.To = target + dir * dist;
        if (avatar.Body != null && !camRayExcludesSelf)
        {
            query.Exclude = new Godot.Collections.Array<Rid> { avatar.Body.GetRid() };
            camRayExcludesSelf = true;
        }
        var hit = context.Root.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return dist;
        float atHit = target.DistanceTo((Vector3)hit["position"]);
        return dist - atHit < CamNearPad * 2f ? dist : Mathf.Max(MinDist, atHit);
    }

    private Vector3 EyeAboveTerrain(Vector3 eye)
    {
        if (scenery.Terrain == null) return eye;
        if (scenery.GroundHeight(eye) is not { } ground) return eye;
        float delta = ground - eye.Y + CamNearPad;
        return delta < 0f ? eye : new Vector3(eye.X, eye.Y + delta, eye.Z);
    }
}
