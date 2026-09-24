using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class World
{
    private int _selectedId = -1;
    private MeshInstance3D _selRing = null!;
    private ShaderMaterial _selRingMat = null!;
    private const float ClickPickRadius = 48f;
    private const float BodyPickPadPx = 3f;
    private const float BodyPickMinPx = 14f;
    private VBoxContainer _targetBox = null!;
    private Label _targetName = null!;
    private StatBar _targetHp = null!;

    private void BuildSelectionRing()
    {
        _selRingMat = TargetSymbol.Material();
        _selRing = new MeshInstance3D
        {
            Mesh = TargetSymbol.Mesh(),
            MaterialOverride = _selRingMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        _entities.AddChild(_selRing);
    }

    private MeshInstance3D _objHighlight = null!;
    private StandardMaterial3D _objHighlightMat = null!;
    private static readonly Color SelectedBoxColor = new(1f, 0.15f, 0.12f);

    private static ArrayMesh BuildWireBoxMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Lines);
        Vector3 C(int i) => new((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f);
        for (int i = 0; i < 8; i++)
            foreach (int bit in new[] { 1, 2, 4 })
                if ((i & bit) == 0) { st.AddVertex(C(i)); st.AddVertex(C(i | bit)); }
        return st.Commit();
    }

    private void BuildObjectHighlight()
    {
        _objHighlightMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = SelectedBoxColor,
            NoDepthTest = true,
        };
        _objHighlight = new MeshInstance3D
        {
            Mesh = BuildWireBoxMesh(),
            Visible = false,
            MaterialOverride = _objHighlightMat,
        };
        AddChild(_objHighlight);
    }

    private void RefreshObjectHighlight()
    {
        if (_objHighlight == null) return;
        if (_infoShown && _pickKind == PickKind.Object && _pickedObject?.HitMesh is { } hitMesh)
        {
            var aabb = hitMesh.GetAabb();
            var local = new Transform3D(Basis.Identity.Scaled(aabb.Size), aabb.GetCenter());
            _objHighlight.GlobalTransform = _pickedObject.HitXform * local;
            _objHighlight.Visible = true;
        }
        else if (_infoShown && _pickKind == PickKind.Effect && _pickedFx is { } fx
                 && GodotObject.IsInstanceValid(fx.Node))
        {
            var box = FxBounds(fx);
            _objHighlight.GlobalTransform = new Transform3D(Basis.Identity.Scaled(box.Size), box.GetCenter());
            _objHighlight.Visible = true;
        }
        else
            _objHighlight.Visible = false;
        RefreshPickMarkers();
    }

    private const float FxBoxMinSize = 0.7f;

    private static Aabb FxBounds(FxRegistry.Entry fx)
    {
        fx.LocalBounds ??= MeasureFxLocalBounds(fx.Node);
        var box = XformAabb(fx.Node.GlobalTransform,
            fx.LocalBounds ?? new Aabb(new Vector3(-0.35f, -0.35f, -0.35f), Vector3.One * 0.7f));
        var size = box.Size;
        float gx = Mathf.Max(0f, FxBoxMinSize - size.X) * 0.5f;
        float gy = Mathf.Max(0f, FxBoxMinSize - size.Y) * 0.5f;
        float gz = Mathf.Max(0f, FxBoxMinSize - size.Z) * 0.5f;
        return new Aabb(box.Position - new Vector3(gx, gy, gz), size + new Vector3(gx, gy, gz) * 2f);
    }

    private const float FxBoxMaxHalf = 12f;

    private static Aabb? MeasureFxLocalBounds(Node3D root)
    {
        var toLocal = root.GlobalTransform.AffineInverse();
        Aabb? acc = null;
        Walk(root);
        if (!acc.HasValue) return null;
        var lo = Clamp(acc.Value.Position);
        var hi = Clamp(acc.Value.Position + acc.Value.Size);
        return new Aabb(lo, hi - lo);

        static Vector3 Clamp(Vector3 v) => new(
            Mathf.Clamp(v.X, -FxBoxMaxHalf, FxBoxMaxHalf),
            Mathf.Clamp(v.Y, -FxBoxMaxHalf, FxBoxMaxHalf),
            Mathf.Clamp(v.Z, -FxBoxMaxHalf, FxBoxMaxHalf));

        void Walk(Node n)
        {
            foreach (var child in n.GetChildren())
            {
                if (child is Node3D n3)
                {
                    var local = toLocal * n3.GlobalTransform;
                    Aabb? box = child switch
                    {
                        Light3D => null,
                        GpuParticles3D => new Aabb(local.Origin - Vector3.One * 0.4f, Vector3.One * 0.8f),
                        VisualInstance3D vi => XformAabb(local, vi.GetAabb()),
                        _ => null,
                    };
                    if (box.HasValue) acc = acc.HasValue ? acc.Value.Merge(box.Value) : box.Value;
                }
                Walk(child);
            }
        }
    }

    private static Aabb XformAabb(Transform3D t, Aabb a)
    {
        var lo = a.Position;
        var hi = a.Position + a.Size;
        var first = t * lo;
        var box = new Aabb(first, Vector3.Zero);
        for (int i = 1; i < 8; i++)
            box = box.Expand(t * new Vector3(
                (i & 1) != 0 ? hi.X : lo.X,
                (i & 2) != 0 ? hi.Y : lo.Y,
                (i & 4) != 0 ? hi.Z : lo.Z));
        return box;
    }

    private static ArrayMesh BuildRingMesh(float inner, float outer, int seg = 48)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < seg; i++)
        {
            float a0 = Mathf.Tau * i / seg, a1 = Mathf.Tau * (i + 1) / seg;
            float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
            var oi0 = new Vector3(c0 * outer, 0, s0 * outer); var oi1 = new Vector3(c1 * outer, 0, s1 * outer);
            var ii0 = new Vector3(c0 * inner, 0, s0 * inner); var ii1 = new Vector3(c1 * inner, 0, s1 * inner);
            foreach (var v in new[] { ii0, oi0, oi1, ii0, oi1, ii1 }) { st.SetNormal(Vector3.Up); st.AddVertex(v); }
        }
        return st.Commit();
    }

    private static bool Selectable(Ent e) => (!e.Dead || !e.IsNpc) && !e.Infiltrating;

    private void UpdateSelectionRing()
    {
        if (_selectedId >= 0 && _ents.TryGetValue(_selectedId, out var e) && Selectable(e))
        {
            var p = e.Body.Position;
            var feet = new Vector3(p.X, p.Y - e.Lift, p.Z);
            TargetSymbol.Place(_selRing, feet, e.BoundRadius, GroundNormalAt(feet));
        }
        else
        {
            _selRing.Visible = false;
            _selectedId = -1;
        }
    }

    private Ent? PickEntityAt(Vector2 mouse, float radius, out int id, out float pixels)
    {
        Ent? best = null;
        Ent? boxed = null;
        int boxedId = -1;
        float boxedDepth = float.MaxValue;
        id = -1;
        pixels = radius;
        if (_camera == null) return null;
        var eye = _camera.GlobalPosition;
        foreach (var kv in _ents)
        {
            var e = kv.Value;
            if (!Selectable(e)) continue;
            var world = e.Body.GlobalPosition + Vector3.Up;
            if (_camera.IsPositionBehind(world)) continue;

            if (BodyScreenRect(e) is { } rect && rect.HasPoint(mouse))
            {
                float depth = eye.DistanceSquaredTo(e.Body.GlobalPosition);
                if (depth < boxedDepth) { boxedDepth = depth; boxed = e; boxedId = kv.Key; }
            }

            float d = _camera.UnprojectPosition(world).DistanceTo(mouse);
            if (d < pixels) { pixels = d; best = e; id = kv.Key; }
        }
        if (boxed == null) return best;
        id = boxedId;
        pixels = 0f;
        return boxed;
    }

    private Rect2? BodyScreenRect(Ent e)
    {
        var box = EntityLocalBox(e);
        var t = e.Body.GlobalTransform;
        var lo = new Vector2(float.MaxValue, float.MaxValue);
        var hi = new Vector2(float.MinValue, float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            var corner = t * (box.Position + new Vector3(
                (c & 1) != 0 ? box.Size.X : 0f,
                (c & 2) != 0 ? box.Size.Y : 0f,
                (c & 4) != 0 ? box.Size.Z : 0f));
            if (_camera.IsPositionBehind(corner)) return null;
            var p = _camera.UnprojectPosition(corner);
            lo = new Vector2(Mathf.Min(lo.X, p.X), Mathf.Min(lo.Y, p.Y));
            hi = new Vector2(Mathf.Max(hi.X, p.X), Mathf.Max(hi.Y, p.Y));
        }
        var size = hi - lo;
        float gx = Mathf.Max(BodyPickPadPx, (BodyPickMinPx - size.X) * 0.5f);
        float gy = Mathf.Max(BodyPickPadPx, (BodyPickMinPx - size.Y) * 0.5f);
        return new Rect2(lo - new Vector2(gx, gy), size + new Vector2(gx, gy) * 2f);
    }

    private bool TryPickAt(Vector2 mouse)
    {
        var best = PickEntityAt(mouse, ClickPickRadius, out int bestId, out float bestD);
        if (best != null && _infoShown && _pickKind == PickKind.Effect && NearestFxPixels(mouse) < bestD)
            best = null;
        if (best != null)
        {
            bool retarget = bestId != _selectedId;
            Select(bestId, best);
            if (!retarget && !best.Attackable) TalkToNpc(bestId);
            return true;
        }
        if (TrySelectWarpGate(mouse)) return true;
        if (TrySelectAnvil(mouse)) return true;
        Deselect();
        PickAt(mouse);
        return false;
    }

    private enum PickKind { Object, Effect }
    private PickKind _pickKind = PickKind.Object;

    private readonly List<ObjInfo> _objCands = new();
    private readonly List<FxRegistry.Entry> _fxCands = new();
    private int _candIdx;
    private Vector2 _candPixel = new(-9999f, -9999f);
    private Transform3D _candCam;
    private const float CandSamePixel = 6f;

    private int CandCount => _pickKind == PickKind.Object ? _objCands.Count : _fxCands.Count;

    private void SetPickKind(PickKind kind)
    {
        if (_pickKind == kind) return;
        _pickKind = kind;
        _pickedObject = null;
        _pickedFx = null;
        ClearPickCandidates();
        RefreshObjectHighlight();
        _infoNextRebuild = 0;
    }

    private void ClearPickCandidates()
    {
        _objCands.Clear();
        _fxCands.Clear();
        _candIdx = 0;
        _candPixel = new Vector2(-9999f, -9999f);
    }

    private void PickAt(Vector2 mouse)
    {
        if (_camera == null) return;
        bool sameSpot = CandCount > 0
                        && mouse.DistanceTo(_candPixel) <= CandSamePixel
                        && _camera.GlobalTransform.Origin.DistanceSquaredTo(_candCam.Origin) < 0.0025f
                        && _camera.GlobalBasis.Z.DistanceSquaredTo(_candCam.Basis.Z) < 1e-6f;
        if (sameSpot)
        {
            _candIdx = (_candIdx + 1) % CandCount;
        }
        else
        {
            _objCands.Clear();
            _fxCands.Clear();
            _candIdx = 0;
            if (_pickKind == PickKind.Object) GatherObjectCandidates(mouse, _objCands);
            else GatherFxCandidates(mouse, _fxCands);
            _candPixel = mouse;
            _candCam = _camera.GlobalTransform;
        }
        ApplyPickCandidate();
    }

    private void CyclePick(int step)
    {
        int n = CandCount;
        if (n == 0) return;
        _candIdx = ((_candIdx + step) % n + n) % n;
        ApplyPickCandidate();
    }

    private void ApplyPickCandidate()
    {
        if (_pickKind == PickKind.Object)
        {
            _pickedFx = null;
            _pickedObject = _candIdx < _objCands.Count ? _objCands[_candIdx] : null;
        }
        else
        {
            _pickedObject = null;
            _pickedFx = _candIdx < _fxCands.Count ? _fxCands[_candIdx] : null;
        }
        RefreshObjectHighlight();
        _infoNextRebuild = 0;
    }

    private void GatherObjectCandidates(Vector2 mouse, List<ObjInfo> into)
    {
        var from = _camera.ProjectRayOrigin(mouse);
        var dir = _camera.ProjectRayNormal(mouse);
        var hits = new List<(ObjInfo Obj, float Dist, bool Exact)>();
        foreach (var o in _objects)
        {
            if (o.HitMesh is not { } mesh) continue;
            var xform = o.HitXform;
            var inv = xform.AffineInverse();
            Vector3 lo = inv * from;
            Vector3 ld = inv.Basis * dir;
            if (!RayAabbEntry(lo, ld, mesh.GetAabb(), out float t)) continue;
            bool exact = RayMeshHit(mesh, lo, ld, out float tTri);
            if (exact) t = tTri;
            Vector3 worldHit = xform * (lo + ld * t);
            hits.Add((o, (worldHit - from).Length(), exact));
        }
        hits.Sort((a, b) => a.Exact != b.Exact
            ? (a.Exact ? -1 : 1)
            : a.Dist.CompareTo(b.Dist));
        foreach (var h in hits) into.Add(h.Obj);
    }

    private readonly Dictionary<Mesh, (Vector3[] Verts, int[] Idx)> _pickTris = new();
    private const int PickTriCacheMax = 96;
    private const int PickTriLimit = 300_000;

    private bool RayMeshHit(Mesh mesh, Vector3 o, Vector3 d, out float t)
    {
        t = 0f;
        if (!_pickTris.TryGetValue(mesh, out var soup))
        {
            if (_pickTris.Count >= PickTriCacheMax) _pickTris.Clear();
            soup = BuildTriSoup(mesh);
            _pickTris[mesh] = soup;
        }
        if (soup.Idx.Length < 3) return false;
        float best = float.MaxValue;
        for (int i = 0; i + 2 < soup.Idx.Length; i += 3)
            if (RayTriangle(o, d, soup.Verts[soup.Idx[i]], soup.Verts[soup.Idx[i + 1]], soup.Verts[soup.Idx[i + 2]],
                    out float ti) && ti < best)
                best = ti;
        if (best == float.MaxValue) return false;
        t = best;
        return true;
    }

    private static (Vector3[] Verts, int[] Idx) BuildTriSoup(Mesh mesh)
    {
        var verts = new List<Vector3>();
        var idx = new List<int>();
        int surfaces = mesh.GetSurfaceCount();
        for (int s = 0; s < surfaces; s++)
        {
            if (mesh is ArrayMesh am && am.SurfaceGetPrimitiveType(s) != Mesh.PrimitiveType.Triangles) continue;
            var arrays = mesh.SurfaceGetArrays(s);
            var pv = arrays[(int)Mesh.ArrayType.Vertex];
            if (pv.VariantType != Variant.Type.PackedVector3Array) continue;
            var sv = pv.AsVector3Array();
            int baseIdx = verts.Count;
            verts.AddRange(sv);
            var iv = arrays[(int)Mesh.ArrayType.Index];
            if (iv.VariantType != Variant.Type.Nil)
                foreach (int i in iv.AsInt32Array()) idx.Add(baseIdx + i);
            else
                for (int i = 0; i < sv.Length; i++) idx.Add(baseIdx + i);
            if (idx.Count / 3 > PickTriLimit) return (System.Array.Empty<Vector3>(), System.Array.Empty<int>());
        }
        return (verts.ToArray(), idx.ToArray());
    }

    private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0f;
        Vector3 e1 = b - a, e2 = c - a, p = d.Cross(e2);
        float det = e1.Dot(p);
        if (Mathf.Abs(det) < 1e-12f) return false;
        float inv = 1f / det;
        Vector3 s = o - a;
        float u = s.Dot(p) * inv;
        if (u < -1e-6f || u > 1f + 1e-6f) return false;
        Vector3 q = s.Cross(e1);
        float v = d.Dot(q) * inv;
        if (v < -1e-6f || u + v > 1f + 1e-6f) return false;
        t = e2.Dot(q) * inv;
        return t >= 0f;
    }

    private const float FxPickRadius = 34f;
    private const float FxPickMaxDist = 400f;

    private void GatherFxCandidates(Vector2 mouse, List<FxRegistry.Entry> into)
    {
        var cam = _camera.GlobalPosition;
        var hits = new List<(FxRegistry.Entry Fx, float Dist, float Px)>();
        foreach (var fx in FxRegistry.Live)
        {
            if (fx.Node == _fxPreview || fx.Node == _fxDebug) continue;
            var box = FxBounds(fx);
            float dist = cam.DistanceTo(box.GetCenter());
            if (dist > FxPickMaxDist) continue;
            float px = FxScreenDist(box, mouse);
            if (px > FxPickRadius) continue;
            hits.Add((fx, dist, px));
        }
        hits.Sort((a, b) => Mathf.Abs(a.Dist - b.Dist) > 0.5f ? a.Dist.CompareTo(b.Dist) : a.Px.CompareTo(b.Px));
        foreach (var h in hits) into.Add(h.Fx);
    }

    private float NearestFxPixels(Vector2 mouse)
    {
        if (_camera == null) return float.MaxValue;
        var cam = _camera.GlobalPosition;
        float best = float.MaxValue;
        foreach (var fx in FxRegistry.Live)
        {
            if (fx.Node == _fxPreview || fx.Node == _fxDebug) continue;
            var box = FxBounds(fx);
            if (cam.DistanceTo(box.GetCenter()) > FxPickMaxDist) continue;
            float px = FxScreenDist(box, mouse);
            if (px <= FxPickRadius && px < best) best = px;
        }
        return best;
    }

    private float FxScreenDist(Aabb box, Vector2 mouse)
    {
        var lo = box.Position;
        var hi = box.Position + box.Size;
        Rect2? rect = null;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) != 0 ? hi.X : lo.X,
                (i & 2) != 0 ? hi.Y : lo.Y,
                (i & 4) != 0 ? hi.Z : lo.Z);
            if (_camera.IsPositionBehind(corner)) continue;
            var p = _camera.UnprojectPosition(corner);
            rect = rect.HasValue ? rect.Value.Expand(p) : new Rect2(p, Vector2.Zero);
        }
        if (!rect.HasValue) return float.MaxValue;
        var r = rect.Value;
        float dx = Mathf.Max(Mathf.Max(r.Position.X - mouse.X, mouse.X - r.End.X), 0f);
        float dy = Mathf.Max(Mathf.Max(r.Position.Y - mouse.Y, mouse.Y - r.End.Y), 0f);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static bool RayAabbEntry(Vector3 o, Vector3 d, Aabb box, out float t)
    {
        t = 0f;
        float tmin = float.NegativeInfinity, tmax = float.PositiveInfinity;
        Vector3 lo = box.Position, hi = box.Position + box.Size;
        for (int a = 0; a < 3; a++)
        {
            float oa = o[a], da = d[a];
            if (Mathf.Abs(da) < 1e-9f)
            {
                if (oa < lo[a] || oa > hi[a]) return false;
            }
            else
            {
                float inv = 1f / da;
                float t1 = (lo[a] - oa) * inv, t2 = (hi[a] - oa) * inv;
                if (t1 > t2) (t1, t2) = (t2, t1);
                tmin = Mathf.Max(tmin, t1);
                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
        }
        if (tmax < 0f) return false;
        t = Mathf.Max(tmin, 0f);
        return true;
    }

    private void SelectNearest(bool hostile)
    {
        Ent? best = null; int bestId = -1; float bestD = float.MaxValue;
        foreach (var kv in _ents)
        {
            var e = kv.Value;
            if (e.Dead || e.Attackable != hostile) continue;
            if (!hostile && !e.IsNpc) continue;
            float d = e.Body.Position.DistanceTo(_self.Position);
            if (d < bestD) { bestD = d; best = e; bestId = kv.Key; }
        }
        if (best != null) Select(bestId, best);
    }

    private void FaceSelectedWhenStill()
    {
        if (_self == null || _selfDead || _selfMoving || IsRootedByCast()) return;
        if (_selectedId < 0 || !_ents.TryGetValue(_selectedId, out var e)) return;
        FaceSelfToward(e.Body.Position);
        SendHeadingIfChanged(_myKoX, _myKoZ);
    }

    private void Select(int id, Ent e)
    {
        if (_autoAttack && _autoTargetId != id)
            StopAutoAttack();
        _selectedId = id;
        _selfClip = null;
        _pickedObject = null;
        _pickedFx = null;
        SelectWarpGate(null);
        SelectAnvil(null);
        ClearPickCandidates();
        RefreshObjectHighlight();
        _infoNextRebuild = 0;
        TargetSymbol.Tint(_selRingMat, e.Attackable
            ? new Color(0.95f, 0.2f, 0.2f)
            : new Color(0.25f, 0.9f, 0.3f));
        UpdateSelectionRing();
    }

    private void Deselect()
    {
        _selectedId = -1;
        _selfClip = null;
        SelectWarpGate(null);
        SelectAnvil(null);
        StopAutoAttack();
    }

    private const float TargetHudWidth = 240f;
    private const float TargetHudBarHeight = 22f;
    private const float TargetCloseSize = 26f;

    private void BuildTargetHud()
    {
        var layer = new CanvasLayer { Layer = 64 };
        AddChild(layer);
        _targetBox = new VBoxContainer { Visible = false };
        _targetBox.AddThemeConstantOverride("separation", 3);
        layer.AddChild(_targetBox);
        _targetName = HudStyle.Label(16, HorizontalAlignment.Center);
        if (Platform.TouchUi)
        {
            _targetBox.AddChild(BuildTargetHead());
        }
        else
        {
            _targetName.CustomMinimumSize = new Vector2(TargetHudWidth, 0);
            _targetBox.AddChild(_targetName);
        }
        _targetHp = new StatBar(new Color("c0392b"),
                                new Vector2(TargetHudWidth, TargetHudBarHeight));
        _targetBox.AddChild(_targetHp);
        HudLayout.Attach(_targetBox, "hud_target", _targetName,
            () =>
            {
                float vw = GetViewport().GetVisibleRect().Size.X;
                return new Vector2(Mathf.Max(0f, (vw - TargetHudWidth) * 0.5f), 10f);
            },
            anchor: HudPlacement.TargetAnchor, anchorMargin: HudPlacement.TargetMargin,
            anchorSize: new Vector2(TargetHudWidth, 0f));
        if (Platform.TouchUi)
        {
            _targetBox.Scale = Vector2.One * HudPlacement.TargetScale;
            _targetBox.PivotOffset = new Vector2(TargetHudWidth * 0.5f, 0f);
        }
    }

    private Control BuildTargetHead()
    {
        var head = new HBoxContainer { CustomMinimumSize = new Vector2(TargetHudWidth, 0) };
        head.AddThemeConstantOverride("separation", 0);
        head.AddChild(new Control { CustomMinimumSize = new Vector2(TargetCloseSize, 0) });

        _targetName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _targetName.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_targetName);

        var close = new Button
        {
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(TargetCloseSize, TargetCloseSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            TooltipText = "Clear target",
        };
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            close.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        var glyph = new CloseGlyph { MouseFilter = Control.MouseFilterEnum.Ignore };
        glyph.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        close.AddChild(glyph);
        close.Pressed += Deselect;
        head.AddChild(close);
        return head;
    }

    private int _lastHitId = -1;
    private double _lastHitAt;
    private const double LastHitHudDur = 8.0;

    private const double TargetHpPollInterval = 0.5;
    private double _targetHpPollAt;

    private void TargetHpPollTick(double now)
    {
        if (_selectedId < 0 || now < _targetHpPollAt) return;
        _targetHpPollAt = now + TargetHpPollInterval;
        if (_ents.TryGetValue(_selectedId, out var e) && !e.Dead)
            Net.I.SendTargetHpRequest(_selectedId);
    }

    private void OnEntityHpSync(int id, int hp, int maxHp)
    {
        if (!_ents.TryGetValue(id, out var e)) return;
        e.Hp = hp;
        e.MaxHp = maxHp;
        UpdateEntHpBar(e);
    }

    private void UpdateTargetHud()
    {
        int show = _selectedId >= 0 && _ents.ContainsKey(_selectedId)
            ? _selectedId
            : (_lastHitId >= 0 && Now() <= _lastHitAt + LastHitHudDur ? _lastHitId : -1);
        if (show >= 0 && _ents.TryGetValue(show, out var e))
        {
            _targetName.Text = e.Level > 0 ? $"{e.Name}   Lv {e.Level}" : e.Name;
            if (e.MaxHp > 0) _targetHp.Set(e.Hp, e.MaxHp); else _targetHp.SetFull();
            _targetBox.Visible = true;
        }
        else _targetBox.Visible = false;
    }
}
