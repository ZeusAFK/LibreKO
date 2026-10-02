using Godot;
using System.Collections.Generic;

namespace LibreKO;

public partial class FxInstance : Node3D
{
    private const int VisibilityRefreshFrames = 4;

    public float BundleLife;
    private float _age;

    public float AuthoredVelocity;

    public Node3D? FollowNode;
    public Node3D? FacingNode;
    public Vector3 FollowOffset;
    public Vector3 FollowDirection = Vector3.Back;
    public bool InheritFollowScale = true;

    public bool Asleep;
    internal bool Driven { get; private set; }
    private bool _registered, _released;
    internal int PendingParts;
    internal bool Released => _released;
    private FxDriver? _driver;
    private int _visibilityCountdown;
    private Transform3D _pinned;
    private bool _pinnedValid;
    private readonly List<IFxPart> _parts = new();

    public void Pin(Node3D anchor, Node3D facing, Vector3 offset, Vector3 direction,
        bool inheritScale = true)
    {
        TopLevel = true;
        FollowNode = anchor;
        FacingNode = facing;
        FollowOffset = offset;
        FollowDirection = direction;
        InheritFollowScale = inheritScale;
        _pinnedValid = false;
        TickAim(0f);
    }

    public Node3D? HomingTarget;
    public Vector3 HomingPoint;
    public float HomingHeight;
    public float FlightSpeed;
    public float ArriveRadius = 0.6f;
    public float MaxFlightTime = 4f;
    private float _flightAge;

    public void Release()
    {
        if (_released) return;
        _released = true;
        foreach (var child in GetChildren())
            if (child is Node3D part and IFxPooledPart) Fx.RecyclePart(part);
        QueueFree();
    }

    internal void AddPart(IFxPart part) => _parts.Add(part);

    internal void RemovePart(IFxPart part) => _parts.Remove(part);

    private bool _layerUser;

    public override void _EnterTree()
    {
        Driven = true;
        if (!_layerUser) { _layerUser = true; FxLayer.Users++; }
        if (_registered) return;
        _driver = FxDriver.For(this);
        if (_driver == null) return;
        _driver.Add(this);
        _registered = true;
        _visibilityCountdown = 0;
    }

    public override void _ExitTree()
    {
        Driven = false;
        if (_layerUser) { _layerUser = false; FxLayer.Users--; }
    }

    internal void Unregistered() => _registered = false;

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

    private ulong _frame = ulong.MaxValue;
    private bool _inverseValid, _frameInverseValid;
    private Transform3D _inverse;
    private Basis _frameInverse = Basis.Identity;
    internal Transform3D FrameXf;
    internal Transform3D CamXf;
    internal bool HasCam;
    internal bool VisibleInTree = true;

    internal Basis FrameInverse
    {
        get
        {
            if (!_frameInverseValid) { _frameInverse = FrameXf.Basis.Orthonormalized().Inverse(); _frameInverseValid = true; }
            return _frameInverse;
        }
    }

    internal Transform3D FrameXfInverse
    {
        get
        {
            if (!_inverseValid) { _inverse = FrameXf.AffineInverse(); _inverseValid = true; }
            return _inverse;
        }
    }

    internal void EnsureFrame()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame == _frame) return;
        _frame = frame;
        FrameXf = GlobalTransform;
        HasCam = Fx.FrameCamera(this, out CamXf);
        VisibleInTree = IsVisibleInTree();
        _inverseValid = _frameInverseValid = false;
    }

    internal void Tick(double delta, ulong frame, bool hasCam, in Transform3D camXf)
    {
        if (!TickSelf(delta, frame, hasCam, camXf)) return;
        for (int i = 0; i < _parts.Count;)
        {
            var part = _parts[i];
            part.Tick(delta);
            if (i < _parts.Count && ReferenceEquals(_parts[i], part)) i++;
        }
    }

    private bool TickSelf(double delta, ulong frame, bool hasCam, in Transform3D camXf)
    {
        using var scope = Perf.Measure(Perf.Section.FxRoot);
        if (_released || Asleep || Fx.ShuttingDown) return false;
        if (FollowNode != null || FlightSpeed > 0f)
        {
            TickAim((float)delta);
            if (_released) return false;
        }
        _frame = frame;
        FrameXf = _pinnedValid ? _pinned : GlobalTransform;
        _inverseValid = _frameInverseValid = false;
        HasCam = hasCam;
        CamXf = camXf;
        if (--_visibilityCountdown <= 0)
        {
            VisibleInTree = IsVisibleInTree();
            _visibilityCountdown = VisibilityRefreshFrames;
        }
        if (VisibleInTree && _driver != null && _driver.Sees(FrameXf.Origin)) Perf.CountFxVisible();
        if (BundleLife > 0.001f && PendingParts == 0)
        {
            _age += (float)delta;
            if (_age > BundleLife) { Release(); return false; }
        }
        return true;
    }

    private void TickAim(float delta)
    {
        if (FollowNode != null)
        {
            if (!GodotObject.IsInstanceValid(FollowNode)) { FollowNode = null; _pinnedValid = false; return; }
            if (!IsInsideTree() || !FollowNode.IsInsideTree()) { _pinnedValid = false; return; }
            var follow = FollowNode.GlobalTransform;
            Vector3 facing;
            if (FacingNode == FollowNode) facing = follow.Basis * FollowDirection;
            else if (FacingNode != null && GodotObject.IsInstanceValid(FacingNode)) facing = FacingNode.GlobalBasis * FollowDirection;
            else facing = _pinnedValid ? _pinned.Basis.Z : GlobalBasis.Z;
            var basis = AimBasis(facing);
            float scale = InheritFollowScale ? follow.Basis.Scale.Y : 1f;
            if (scale <= 0.001f) scale = 1f;
            var pinned = new Transform3D(basis.Scaled(Vector3.One * scale), follow * FollowOffset);
            if (!_pinnedValid || pinned != _pinned)
            {
                GlobalTransform = pinned;
                _pinned = pinned;
                _pinnedValid = true;
            }
            return;
        }

        _pinnedValid = false;
        _flightAge += delta;
        Vector3 aim = HomingTarget != null && GodotObject.IsInstanceValid(HomingTarget)
            ? HomingTarget.GlobalPosition + new Vector3(0, HomingHeight, 0)
            : HomingPoint;
        Vector3 to = aim - GlobalPosition;
        if (to.Length() <= ArriveRadius || _flightAge > MaxFlightTime) { Release(); return; }
        Vector3 dir = to.Normalized();
        GlobalPosition += dir * FlightSpeed * delta;
        GlobalBasis = AimBasis(dir).Scaled(GlobalBasis.Scale);
    }
}
