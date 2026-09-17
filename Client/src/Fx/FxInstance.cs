using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxInstance : Node3D
{
    public float BundleLife;
    private float _age;

    public float AuthoredVelocity;

    public Node3D? FollowNode;
    public Node3D? FacingNode;
    public Vector3 FollowOffset;
    public Vector3 FollowDirection = Vector3.Back;
    public bool InheritFollowScale = true;

    public void Pin(Node3D anchor, Node3D facing, Vector3 offset, Vector3 direction,
        bool inheritScale = true)
    {
        TopLevel = true;
        FollowNode = anchor;
        FacingNode = facing;
        FollowOffset = offset;
        FollowDirection = direction;
        InheritFollowScale = inheritScale;
        TickAim(0f);
    }

    public Node3D? HomingTarget;
    public Vector3 HomingPoint;
    public float HomingHeight;
    public float FlightSpeed;
    public float ArriveRadius = 0.6f;
    public float MaxFlightTime = 4f;
    private float _flightAge;

    public static Basis AimBasis(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-8f) return Basis.Identity;
        dir = dir.Normalized();
        Vector3 fwd = Vector3.Back;
        Vector3 axis = new(Mathf.Snapped(fwd.Cross(dir).X, 1e-4f),
                           Mathf.Snapped(fwd.Cross(dir).Y, 1e-4f),
                           Mathf.Snapped(fwd.Cross(dir).Z, 1e-4f));
        if (axis.LengthSquared() < 1e-12f) axis = Vector3.Up;
        return new Basis(axis.Normalized(), Mathf.Acos(Mathf.Clamp(fwd.Dot(dir), -1f, 1f)));
    }

    public override void _Process(double delta)
    {
        if (Fx.ShuttingDown || IsQueuedForDeletion()) return;
        if (FollowNode != null || FlightSpeed > 0f) TickAim((float)delta);
        if (BundleLife <= 0.001f) return;
        _age += (float)delta;
        if (_age > BundleLife) QueueFree();
    }

    private void TickAim(float delta)
    {
        if (FollowNode != null)
        {
            if (!GodotObject.IsInstanceValid(FollowNode)) { FollowNode = null; return; }
            var facing = FacingNode != null && GodotObject.IsInstanceValid(FacingNode)
                ? FacingNode.GlobalBasis * FollowDirection
                : GlobalBasis.Z;
            var basis = AimBasis(facing);
            float scale = InheritFollowScale ? FollowNode.GlobalBasis.Scale.Y : 1f;
            if (scale <= 0.001f) scale = 1f;
            GlobalTransform = new Transform3D(basis.Scaled(Vector3.One * scale), FollowNode.ToGlobal(FollowOffset));
            return;
        }

        _flightAge += delta;
        Vector3 aim = HomingTarget != null && GodotObject.IsInstanceValid(HomingTarget)
            ? HomingTarget.GlobalPosition + new Vector3(0, HomingHeight, 0)
            : HomingPoint;
        Vector3 to = aim - GlobalPosition;
        if (to.Length() <= ArriveRadius || _flightAge > MaxFlightTime) { QueueFree(); return; }
        Vector3 dir = to.Normalized();
        GlobalPosition += dir * FlightSpeed * delta;
        GlobalBasis = AimBasis(dir).Scaled(GlobalBasis.Scale);
    }
}
