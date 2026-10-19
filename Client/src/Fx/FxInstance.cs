using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxInstance : Node3D
{
    private const int VisibilityRefreshFrames = 4;

    public float BundleLife;
    private float age;

    public float AuthoredVelocity;

    public Node3D? FollowNode;
    public Node3D? FacingNode;
    public Vector3 FollowOffset;
    public Vector3 FollowDirection = Vector3.Back;
    public bool InheritFollowScale = true;
    public bool FollowRotation;

    public bool Asleep;
    internal bool Driven { get; private set; }
    private bool registered, released;
    internal int PendingParts;
    internal bool Released => released;
    private FxDriver? driver;
    private int visibilityCountdown;
    private Transform3D pinned;
    private bool pinnedValid;
    private readonly List<IFxPart> parts = new();

    public void Pin(Node3D anchor, Node3D facing, Vector3 offset, Vector3 direction,
        bool inheritScale = true, bool followRotation = false)
    {
        TopLevel = true;
        FollowNode = anchor;
        FacingNode = facing;
        FollowOffset = offset;
        FollowDirection = direction;
        InheritFollowScale = inheritScale;
        FollowRotation = followRotation;
        pinnedValid = false;
        TickAim(0f);
    }

    public Node3D? HomingTarget;
    public Vector3 HomingPoint;
    public float HomingHeight;
    public float FlightSpeed;
    public float ArriveRadius = 0.6f;
    public float MaxFlightTime = 4f;
    private float flightAge;

    public void Release()
    {
        if (released) return;
        released = true;
        foreach (var child in GetChildren())
            if (child is Node3D part and IFxPooledPart) Fx.RecyclePart(part);
        QueueFree();
    }

    internal void AddPart(IFxPart part) => parts.Add(part);

    internal void RemovePart(IFxPart part) => parts.Remove(part);

    private bool layerUser;

    public override void _EnterTree()
    {
        Driven = true;
        if (!layerUser) { layerUser = true; FxLayer.Users++; }
        if (registered) return;
        driver = FxDriver.For(this);
        if (driver == null) return;
        driver.Add(this);
        registered = true;
        visibilityCountdown = 0;
    }

    public override void _ExitTree()
    {
        Driven = false;
        if (layerUser) { layerUser = false; FxLayer.Users--; }
    }

    internal void Unregistered() => registered = false;

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

    private ulong frame = ulong.MaxValue;
    private bool inverseValid, frameInverseValid;
    private Transform3D inverse;
    private Basis frameInverse = Basis.Identity;
    internal Transform3D FrameXf;
    internal Transform3D CamXf;
    internal bool HasCam;
    internal bool VisibleInTree = true;

    internal Basis FrameInverse
    {
        get
        {
            if (!frameInverseValid) { frameInverse = FrameXf.Basis.Orthonormalized().Inverse(); frameInverseValid = true; }
            return frameInverse;
        }
    }

    internal Transform3D FrameXfInverse
    {
        get
        {
            if (!inverseValid) { inverse = FrameXf.AffineInverse(); inverseValid = true; }
            return inverse;
        }
    }

    internal void EnsureFrame()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame == this.frame) return;
        this.frame = frame;
        FrameXf = GlobalTransform;
        HasCam = Fx.FrameCamera(this, out CamXf);
        VisibleInTree = IsVisibleInTree();
        inverseValid = frameInverseValid = false;
    }

    internal void Tick(double delta, ulong frame, bool hasCam, in Transform3D camXf)
    {
        if (!TickSelf(delta, frame, hasCam, camXf)) return;
        for (int i = 0; i < parts.Count;)
        {
            var part = parts[i];
            part.Tick(delta);
            if (i < parts.Count && ReferenceEquals(parts[i], part)) i++;
        }
    }

    private bool TickSelf(double delta, ulong frame, bool hasCam, in Transform3D camXf)
    {
        using var scope = Perf.Measure(Perf.Section.FxRoot);
        if (released || Asleep || Fx.ShuttingDown) return false;
        if (FollowNode != null || FlightSpeed > 0f)
        {
            TickAim((float)delta);
            if (released) return false;
        }
        this.frame = frame;
        FrameXf = pinnedValid ? pinned : GlobalTransform;
        inverseValid = frameInverseValid = false;
        HasCam = hasCam;
        CamXf = camXf;
        if (--visibilityCountdown <= 0)
        {
            VisibleInTree = IsVisibleInTree();
            visibilityCountdown = VisibilityRefreshFrames;
        }
        if (VisibleInTree && driver != null && driver.Sees(FrameXf.Origin)) Perf.CountFxVisible();
        if (BundleLife > 0.001f && PendingParts == 0)
        {
            age += (float)delta;
            if (age > BundleLife) { Release(); return false; }
        }
        return true;
    }

    private void TickAim(float delta)
    {
        if (FollowNode != null)
        {
            if (!GodotObject.IsInstanceValid(FollowNode)) { FollowNode = null; pinnedValid = false; return; }
            if (!IsInsideTree() || !FollowNode.IsInsideTree()) { pinnedValid = false; return; }
            var follow = FollowNode.GlobalTransform;
            Basis basis;
            if (FollowRotation) basis = follow.Basis.Orthonormalized() * AimBasis(FollowDirection);
            else
            {
                Vector3 facing;
                if (FacingNode == FollowNode) facing = follow.Basis * FollowDirection;
                else if (FacingNode != null && GodotObject.IsInstanceValid(FacingNode)) facing = FacingNode.GlobalBasis * FollowDirection;
                else facing = pinnedValid ? this.pinned.Basis.Z : GlobalBasis.Z;
                basis = AimBasis(facing);
            }
            float scale = InheritFollowScale ? follow.Basis.Scale.Y : 1f;
            if (scale <= 0.001f) scale = 1f;
            var pinned = new Transform3D(basis.Scaled(Vector3.One * scale), follow * FollowOffset);
            if (!pinnedValid || pinned != this.pinned)
            {
                GlobalTransform = pinned;
                this.pinned = pinned;
                pinnedValid = true;
            }
            return;
        }

        pinnedValid = false;
        flightAge += delta;
        Vector3 aim = HomingTarget != null && GodotObject.IsInstanceValid(HomingTarget)
            ? HomingTarget.GlobalPosition + new Vector3(0, HomingHeight, 0)
            : HomingPoint;
        Vector3 to = aim - GlobalPosition;
        if (to.Length() <= ArriveRadius || flightAge > MaxFlightTime) { Release(); return; }
        Vector3 dir = to.Normalized();
        GlobalPosition += dir * FlightSpeed * delta;
        GlobalBasis = AimBasis(dir).Scaled(GlobalBasis.Scale);
    }
}
