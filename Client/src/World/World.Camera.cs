using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const float MinDist = 1.5f;
    private const float MaxDist = 8.0f;
    private const float InfoMaxDist = 100.0f;

    private const float OrbitSens = 0.006f;
    private const float PadLookSpeed = 2.6f;
    private const float MinPitch = 0.20f;
    private const float MaxPitch = 1.45f;
    private Camera3D _camera = null!;

    private float _camYaw;
    private float _camPitch = 0.5f;
    private float _camDist = MaxDist;
    private bool _testCameraFrozen;
    private bool _halfTurnActive;
    private float _halfTurnElapsed;
    private float _halfTurnStartYaw;
    private const float CamAimHeight = 1.85f;
    private const float CamMinAimHeight = 0.9f;
    private const float CamEdgeMargin = 2f;
    private const float CamNearPad = 0.7f;
    private const int CamTerrainSteps = 10;
    private const float CamReturnSpeed = 6f;
    private float _camDistClear = MaxDist;

    private static float CameraHalfTurnDuration =>
        180f / Mathf.Max(1f, Config.CamTurnSpeed);

    internal void FreezeTestCamera(bool frozen) => _testCameraFrozen = frozen;

    private void StartCameraHalfTurn()
    {
        if (_halfTurnActive)
            return;

        _halfTurnActive = true;
        _halfTurnElapsed = 0f;
        _halfTurnStartYaw = _camYaw;
    }

    private void CameraEdgePanTick(double delta)
    {
        if (!Config.CamEdgePan || _hudEditMode) return;
        if (DisplayServer.WindowGetMode() is not (DisplayServer.WindowMode.Fullscreen
            or DisplayServer.WindowMode.ExclusiveFullscreen)) return;

        var viewport = GetViewport();
        if (viewport == null || viewport.GuiGetHoveredControl() != null) return;

        float width = viewport.GetVisibleRect().Size.X;
        float x = viewport.GetMousePosition().X;
        float dir = x <= CamEdgeMargin ? -1f : x >= width - CamEdgeMargin ? 1f : 0f;
        if (dir == 0f) return;

        _halfTurnActive = false;
        _camYaw -= dir * Mathf.DegToRad(Config.CamEdgePanSpeed) * (float)delta;
    }

    private void CameraPadLookTick(double delta)
    {
        var look = KeyBinds.LookStick();
        if (look == Vector2.Zero) return;
        _halfTurnActive = false;
        _camYaw -= look.X * PadLookSpeed * (float)delta;
        _camPitch = Mathf.Clamp(_camPitch + look.Y * PadLookSpeed * (float)delta, MinPitch, MaxPitch);
    }

    private void UpdateCamera(double delta)
    {
        if (_testCameraFrozen) return;
        CameraEdgePanTick(delta);
        CameraPadLookTick(delta);

        if (_halfTurnActive)
        {
            _halfTurnElapsed += (float)delta;
            float t = Mathf.Clamp(_halfTurnElapsed / CameraHalfTurnDuration, 0f, 1f);
            float eased = t * t * (3f - 2f * t);
            _camYaw = _halfTurnStartYaw - Mathf.Pi * eased;
            if (t >= 1f)
            {
                _camYaw = _halfTurnStartYaw - Mathf.Pi;
                _halfTurnActive = false;
            }
        }

        _terrain?.SetDistanceCull(true);
        _camera.Projection = Camera3D.ProjectionType.Perspective;

        float aimT = Mathf.Clamp((_camDist - MinDist) / (MaxDist - MinDist), 0f, 1f);
        float aimHeight = Mathf.Lerp(CamMinAimHeight, CamAimHeight, aimT);
        var target = _self.Position + Vector3.Up * aimHeight;
        float hd = _camDist * Mathf.Cos(_camPitch);
        float h = _camDist * Mathf.Sin(_camPitch);
        var boom = new Vector3(hd * Mathf.Sin(_camYaw), h, hd * Mathf.Cos(_camYaw));
        var dir = boom.Normalized();
        float clear = Mathf.Min(TerrainClearDist(target, dir), ObstacleClearDist(target, dir, _camDist));
        _camDistClear = clear < _camDistClear
            ? clear
            : Mathf.MoveToward(_camDistClear, clear, CamReturnSpeed * (float)delta);
        _camera.Position = EyeAboveTerrain(target + dir * _camDistClear);
        _camera.LookAt(target, Vector3.Up);
    }

    private float TerrainClearDist(Vector3 target, Vector3 dir)
    {
        if (_terrain == null) return _camDist;
        for (int i = 1; i <= CamTerrainSteps; i++)
        {
            float t = _camDist * i / CamTerrainSteps;
            var p = target + dir * t;
            var (koX, koZ) = WorldToKo(p);
            if (!_terrain.SampleHeight(koX, koZ, out float ground)) continue;
            if (p.Y - ground >= CamNearPad) continue;
            return Mathf.Max(MinDist, _camDist * (i - 1) / CamTerrainSteps);
        }
        return _camDist;
    }

    private float ObstacleClearDist(Vector3 target, Vector3 dir, float dist)
    {
        if (NoClip) return dist;
        var query = PhysicsRayQueryParameters3D.Create(target, target + dir * dist, WorldCollisionLayer);
        if (_selfBody != null)
            query.Exclude = new Godot.Collections.Array<Rid> { _selfBody.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return dist;
        float atHit = target.DistanceTo((Vector3)hit["position"]);
        return dist - atHit < CamNearPad * 2f ? dist : Mathf.Max(MinDist, atHit);
    }

    private Vector3 EyeAboveTerrain(Vector3 eye)
    {
        if (_terrain == null) return eye;
        var (koX, koZ) = WorldToKo(eye);
        if (!_terrain.SampleHeight(koX, koZ, out float ground)) return eye;
        float delta = ground - eye.Y + CamNearPad;
        return delta < 0f ? eye : new Vector3(eye.X, eye.Y + delta, eye.Z);
    }

}
