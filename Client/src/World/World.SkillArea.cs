using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private const int AreaRingPointers = 8;
    private const float AreaRingGrowRate = 60f;
    private const float AreaRingSpinDeg = 50f;
    private const float AreaRingInset = 1f;
    private const double AreaAimRepickInterval = 1.0 / 30.0;
    private const double AreaFlashLinger = 0.45;
    private const string AreaRingFx = "zone_pointer";
    private const string AreaMarkerFxKarus = "region_target_ka_wizard";
    private const string AreaMarkerFxElMorad = "region_target_el_wizard";

    private sealed class AreaRing
    {
        public readonly List<Node3D> Pointers = new(AreaRingPointers);
        public Node3D? Marker;
        public Vector3 Centre;
        public float Radius;
        public float Grown;
        public float Spin;
        public double ExpireAt;
    }

    private int _areaSkillId = -1;
    private AreaRing? _areaAim;
    private Vector3 _areaAimPoint;
    private bool _areaAimValid;
    private double _areaRepickAccum;
    private readonly List<AreaRing> _areaFlashes = new();

    private bool AimingAreaSkill => _areaSkillId >= 0;

    private bool BeginAreaCast(SkillData.Skill s)
    {
        if (_self == null || _selfDead) return false;

        CancelAreaCast();
        _areaSkillId = s.Id;
        _areaRepickAccum = AreaAimRepickInterval;
        _areaAimPoint = _self.GlobalPosition;
        _areaAimValid = false;
        _areaAim = NewAreaRing(_areaAimPoint, AreaRingRadius(s), marker: true);
        UpdateAreaAim(GetViewport().GetMousePosition());
        return true;
    }

    private void CancelAreaCast()
    {
        if (_areaSkillId < 0 && _areaAim == null) return;
        _areaSkillId = -1;
        FreeAreaRing(_areaAim);
        _areaAim = null;
        _areaAimValid = false;
    }

    private void ConfirmAreaCast(Vector2 screenPosition)
    {
        var s = SkillData.Get(_areaSkillId);
        UpdateAreaAim(screenPosition);
        Vector3 impact = _areaAimPoint;
        bool valid = _areaAimValid;
        CancelAreaCast();
        if (s == null || _self == null) return;

        if (!valid) return;
        if (impact.DistanceTo(_self.GlobalPosition) > SkillCastRange(s) + 1f) return;

        SendAreaCast(s, impact);
    }

    private void SendAreaCast(SkillData.Skill s, Vector3 impact)
    {
        if (_self == null) return;
        var (koX, koZ) = WorldToKo(impact);
        var data = new short[7];
        data[0] = (short)Mathf.RoundToInt(koX);
        data[1] = (short)Mathf.RoundToInt(impact.Y);
        data[2] = (short)Mathf.RoundToInt(koZ);

        FaceNodeToward(_self, _self.Position, impact);
        Net.I.SendMagic(1, s.Id, -1, data);
        QueuePendingStage(s.Id, -1, PendingEffecting, CastDelay(s), data);
        BeginLocalCast(s);
        FlashAreaRing(impact, AreaRingRadius(s), CastDelay(s) + AreaFlashLinger);
    }

    private static float AreaRingRadius(SkillData.Skill s) => Mathf.Max(1f, s.Radius - AreaRingInset);

    private void FlashAreaRing(Vector3 centre, float radius, double seconds)
    {
        var ring = NewAreaRing(centre, radius, marker: false);
        if (ring == null) return;
        ring.ExpireAt = Now() + seconds;
        _areaFlashes.Add(ring);
    }

    private void AreaCastTick(double delta, double now)
    {
        if (AimingAreaSkill)
        {
            if (_selfDead) CancelAreaCast();
            else
            {
                _areaRepickAccum += delta;
                if (_areaRepickAccum >= AreaAimRepickInterval)
                {
                    _areaRepickAccum = 0;
                    if (GetViewport().GuiGetHoveredControl() == null)
                        UpdateAreaAim(GetViewport().GetMousePosition());
                }
            }
        }

        if (_areaAim != null) AnimateAreaRing(_areaAim, delta);

        for (int i = _areaFlashes.Count - 1; i >= 0; i--)
        {
            var ring = _areaFlashes[i];
            AnimateAreaRing(ring, delta);
            if (now >= ring.ExpireAt) { FreeAreaRing(ring); _areaFlashes.RemoveAt(i); }
        }
    }

    private void UpdateAreaAim(Vector2 screenPosition)
    {
        if (!AimingAreaSkill) return;
        if (!TryPickTerrainPoint(screenPosition, 0f, out Vector3 point)) { _areaAimValid = false; return; }
        _areaAimPoint = point;
        _areaAimValid = true;
        if (_areaAim != null) _areaAim.Centre = point;
    }

    private void AnimateAreaRing(AreaRing ring, double delta)
    {
        if (ring.Grown < ring.Radius)
            ring.Grown = Mathf.Min(ring.Radius, ring.Grown + AreaRingGrowRate * (float)delta);
        ring.Spin += Mathf.DegToRad(AreaRingSpinDeg) * (float)delta;

        if (ring.Marker != null && GodotObject.IsInstanceValid(ring.Marker))
            ring.Marker.GlobalPosition = ring.Centre + Vector3.Up * 0.05f;

        for (int i = 0; i < ring.Pointers.Count; i++)
        {
            var node = ring.Pointers[i];
            if (!GodotObject.IsInstanceValid(node)) continue;
            float angle = ring.Spin + Mathf.Tau * i / ring.Pointers.Count;
            var at = ring.Centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring.Grown;
            node.GlobalPosition = SnapToGround(at) + Vector3.Up * 0.05f;
        }
    }

    private Vector3 SnapToGround(Vector3 world)
    {
        var (koX, koZ) = WorldToKo(world);
        return GroundPos(koX, koZ, world.Y, 0f);
    }

    private AreaRing? NewAreaRing(Vector3 centre, float radius, bool marker)
    {
        var ring = new AreaRing { Centre = centre, Radius = radius };
        for (int i = 0; i < AreaRingPointers; i++)
        {
            var node = Fx.Spawn(AreaRingFx, this, centre);
            if (node == null) break;
            node.Name = $"AreaRingPointer{i}";
            ring.Pointers.Add(node);
        }
        if (marker)
        {
            ring.Marker = Fx.Spawn(AreaMarkerFx(), this, centre + Vector3.Up * 0.05f);
            if (ring.Marker != null) ring.Marker.Name = "AreaCastMarker";
        }
        return ring.Pointers.Count > 0 || ring.Marker != null ? ring : null;
    }

    private static void FreeAreaRing(AreaRing? ring)
    {
        if (ring == null) return;
        foreach (var node in ring.Pointers)
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        ring.Pointers.Clear();
        if (ring.Marker != null && GodotObject.IsInstanceValid(ring.Marker)) ring.Marker.QueueFree();
        ring.Marker = null;
    }

    private string AreaMarkerFx() =>
        _selfClass / 100 == 1 ? AreaMarkerFxKarus : AreaMarkerFxElMorad;

    private bool AreaCastPointer(InputEvent inputEvent, bool overUi)
    {
        if (!AimingAreaSkill) return false;

        if (inputEvent is InputEventMouseMotion motion)
        {
            if (!overUi) UpdateAreaAim(motion.Position);
            return false;
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left } click && !overUi)
        {
            if (click.Pressed) ConfirmAreaCast(click.Position);
            return true;
        }

        if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && !overUi)
        {
            CancelAreaCast();
            return true;
        }

        return false;
    }
}
