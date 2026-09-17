using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private Node3D? _pickMarkerRoot;
    private readonly List<(Label3D Tag, MeshInstance3D Box)> _pickMarkers = new();
    private ArrayMesh? _pickBoxMesh;
    private StandardMaterial3D? _pickBoxMat;
    private double _pickMarkerAccum;

    private const double PickMarkerInterval = 0.15;
    private const int MaxPickMarkers = 64;
    private const float PickMarkerDist = 90f;
    private const float PickMarkerMargin = 60f;

    private static readonly Color ObjMarkerColor = new(1f, 0.88f, 0.45f);
    private static readonly Color FxMarkerColor = new(0.45f, 0.92f, 1f);
    private static readonly Color PickedMarkerColor = new(1f, 0.35f, 0.3f);

    private readonly List<(float Dist, Vector3 At, Transform3D Box, string Text, bool Picked)> _markerScratch = new();

    private void RefreshPickMarkers()
    {
        if (_camera == null) return;
        _markerScratch.Clear();
        if (_infoShown)
        {
            _markerRect = GetViewport().GetVisibleRect().Grow(PickMarkerMargin);
            if (_pickKind == PickKind.Object) CollectObjectMarkers();
            else CollectFxMarkers();
            _markerScratch.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        }
        if (_markerScratch.Count == 0 && _pickMarkerRoot == null) return;

        EnsurePickMarkerRoot();
        var tint = _pickKind == PickKind.Object ? ObjMarkerColor : FxMarkerColor;
        if (_pickBoxMat != null) _pickBoxMat.AlbedoColor = new Color(tint, 0.45f);

        int n = Mathf.Min(_markerScratch.Count, MaxPickMarkers);
        for (int i = 0; i < n; i++)
        {
            var (_, at, box, text, picked) = _markerScratch[i];
            var (tag, mesh) = PickMarker(i);
            tag.Text = text;
            tag.Modulate = picked ? PickedMarkerColor : tint;
            tag.GlobalPosition = at;
            tag.Visible = true;
            mesh.GlobalTransform = box;
            mesh.Visible = !picked;
        }
        for (int i = n; i < _pickMarkers.Count; i++)
        {
            _pickMarkers[i].Tag.Visible = false;
            _pickMarkers[i].Box.Visible = false;
        }
    }

    private void CollectObjectMarkers()
    {
        var cam = _camera.GlobalPosition;
        for (int i = 0; i < _objects.Count; i++)
        {
            var o = _objects[i];
            if (o.HitMesh is not { } mesh) continue;
            var xf = o.HitXform;
            var aabb = mesh.GetAabb();
            var centre = xf * aabb.GetCenter();
            float dist = cam.DistanceTo(centre);
            if (dist > PickMarkerDist || !OnScreen(centre)) continue;
            float half = Mathf.Abs((xf.Basis * new Vector3(0, aabb.Size.Y * 0.5f, 0)).Y);
            _markerScratch.Add((
                dist,
                centre + Vector3.Up * (half + 0.6f),
                xf * new Transform3D(Basis.Identity.Scaled(aabb.Size), aabb.GetCenter()),
                $"#{i} {SafeModelName(o.Name)}",
                o == _pickedObject));
        }
    }

    private void CollectFxMarkers()
    {
        var cam = _camera.GlobalPosition;
        BuildFxOwnerLookup();
        foreach (var fx in FxRegistry.Live)
        {
            if (fx.Node == _fxPreview || fx.Node == _fxDebug) continue;
            var box = FxBounds(fx);
            var centre = box.GetCenter();
            float dist = cam.DistanceTo(centre);
            if (dist > PickMarkerDist || !OnScreen(centre)) continue;
            _markerScratch.Add((
                dist,
                new Vector3(centre.X, box.Position.Y + box.Size.Y + 0.35f, centre.Z),
                new Transform3D(Basis.Identity.Scaled(box.Size), centre),
                FxLabel(fx),
                fx.Node == _pickedFx?.Node));
        }
    }

    private readonly Dictionary<Node3D, string> _fxOwners = new();

    private void BuildFxOwnerLookup()
    {
        _fxOwners.Clear();
        foreach (var kv in _ents)
            if (kv.Value.Body != null)
                _fxOwners[kv.Value.Body] = kv.Value.Name.Length > 0 ? kv.Value.Name : $"entity {kv.Key}";
        if (_self != null) _fxOwners[_self] = "you";
    }

    private string FxLabel(FxRegistry.Entry fx)
    {
        if (_mapFxIndex.TryGetValue(fx.Node, out int idx)) return $"#{idx} {fx.Name}";
        string? owner = FxOwnerName(fx.Node);
        return owner != null ? $"{fx.Name} → {owner}" : fx.Name;
    }

    private string? FxOwnerName(Node3D node)
    {
        for (Node? n = node; n != null && n != this; n = n.GetParent())
            if (n is Node3D n3 && _fxOwners.TryGetValue(n3, out string? name)) return name;
        return null;
    }

    private Rect2 _markerRect;

    private bool OnScreen(Vector3 world) =>
        !_camera.IsPositionBehind(world) && _markerRect.HasPoint(_camera.UnprojectPosition(world));

    private void EnsurePickMarkerRoot()
    {
        if (_pickMarkerRoot != null) return;
        _pickMarkerRoot = new Node3D { Name = "PickMarkers" };
        AddChild(_pickMarkerRoot);
        _pickBoxMesh = BuildWireBoxMesh();
        _pickBoxMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(ObjMarkerColor, 0.45f),
        };
    }

    private (Label3D Tag, MeshInstance3D Box) PickMarker(int i)
    {
        while (_pickMarkers.Count <= i)
        {
            var tag = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 48,
                PixelSize = 0.0032f,
                OutlineSize = 10,
                NoDepthTest = true,
                Visible = false,
            };
            _pickMarkerRoot!.AddChild(tag);
            var box = new MeshInstance3D
            {
                Mesh = _pickBoxMesh,
                MaterialOverride = _pickBoxMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            _pickMarkerRoot.AddChild(box);
            _pickMarkers.Add((tag, box));
        }
        return _pickMarkers[i];
    }
}
