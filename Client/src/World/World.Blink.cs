using Godot;

namespace LibreKO;

public partial class World
{
    private const float BlinkProbeLift = 1f;
    private const float BlinkWallMargin = 2.1f;
    private const float BlinkTerrainStep = 0.5f;
    private const double BlinkWarpWindow = 3.0;

    private double _blinkWarpUntil;

    private void CastBlink(SkillData.Skill s)
    {
        if (_self == null) return;
        var stop = BlinkDestination(s.Radius);
        var (koX, koZ) = WorldToKo(stop);
        var data = BlinkPath.Data(koX, stop.Y, koZ);

        bool instant = SpendInstantMagic(s);
        _blinkWarpUntil = Now() + BlinkWarpWindow;
        Net.I.SendMagic(MagicSub.Casting, s.Id, _myId, data);
        QueuePendingStage(s.Id, _myId, PendingEffecting, instant ? 0 : CastDelay(s), data);
        BeginLocalCast(s, instant);
    }

    private Vector3 BlinkDestination(float reach)
    {
        var start = _self!.GlobalPosition;
        var (dx, dz) = BlinkPath.KoDirection(_self.RotationDegrees.Y);
        var dir = Coord.DirToGodot(dx, 0f, dz);
        var end = start + dir * reach;
        var from = new Vector3(start.X, start.Y - _selfLift + BlinkProbeLift, start.Z);
        var to = new Vector3(end.X, HeightNear(end) + BlinkProbeLift, end.Z);

        float clear = BlinkClearDistance(from, to, reach);
        if (clear < reach) clear = Mathf.Max(0f, clear - BlinkWallMargin);
        var stop = start + dir * clear;
        stop.Y = HeightNear(stop);
        return stop;
    }

    private float BlinkClearDistance(Vector3 from, Vector3 to, float reach)
    {
        float clear = reach;
        var q = PhysicsRayQueryParameters3D.Create(from, to, WorldCollisionLayer);
        if (_selfBody != null) q.Exclude = new Godot.Collections.Array<Rid> { _selfBody.GetRid() };
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count > 0)
        {
            var at = (Vector3)hit["position"];
            clear = new Vector2(at.X - from.X, at.Z - from.Z).Length();
        }

        for (float d = BlinkTerrainStep; d < clear; d += BlinkTerrainStep)
        {
            var p = from.Lerp(to, d / reach);
            if (HeightNear(p) > p.Y) return d;
        }
        return clear;
    }
}
