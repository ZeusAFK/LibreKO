using System.Collections.Generic;
using Godot;

namespace LibreKO;

public interface IFxPart
{
    void Tick(double delta);
}

public partial class FxDriver : Node
{
    private const int DriverProcessPriority = 800;
    private const int FrustumPlanes = 6;

    private static readonly Dictionary<ulong, FxDriver> _perViewport = new();
    private readonly List<FxInstance> _roots = new();
    private readonly Plane[] _planes = new Plane[FrustumPlanes];
    private int _planeCount;

    internal int Roots => _roots.Count;

    internal static FxDriver? For(Node node)
    {
        var viewport = node.GetViewport();
        if (viewport == null) return null;
        ulong id = viewport.GetInstanceId();
        if (_perViewport.TryGetValue(id, out var driver) && GodotObject.IsInstanceValid(driver)) return driver;
        driver = new FxDriver { Name = "FxDriver", ProcessPriority = DriverProcessPriority };
        _perViewport[id] = driver;
        viewport.CallDeferred(Node.MethodName.AddChild, driver);
        return driver;
    }

    internal void Add(FxInstance root) => _roots.Add(root);

    internal bool Sees(Vector3 point)
    {
        if (_planeCount == 0) return false;
        for (int i = 0; i < _planeCount; i++)
            if (_planes[i].IsPointOver(point)) return false;
        return true;
    }

    public override void _Process(double delta)
    {
        if (Fx.ShuttingDown) return;
        var viewport = GetViewport();
        var camera = viewport.GetCamera3D();
        bool hasCam = camera != null;
        var camXf = hasCam ? camera!.GlobalTransform : Transform3D.Identity;
        _planeCount = 0;
        if (hasCam)
        {
            var frustum = camera!.GetFrustum();
            _planeCount = Mathf.Min(frustum.Count, FrustumPlanes);
            for (int i = 0; i < _planeCount; i++) _planes[i] = frustum[i];
        }
        Fx.PrimeFrameCamera(viewport, camera, camXf);
        Fx.BuildPendingParts();
        ulong frame = Engine.GetProcessFrames();
        for (int i = _roots.Count - 1; i >= 0; i--)
        {
            var root = _roots[i];
            if (!root.Driven)
            {
                root.Unregistered();
                _roots[i] = _roots[^1];
                _roots.RemoveAt(_roots.Count - 1);
                continue;
            }
            root.Tick(delta, frame, hasCam, camXf);
        }
    }
}
