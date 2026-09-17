using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private const int WeaponSlices = 8;
    private static readonly NodePath[] WeaponPaths = { new("weapon_6"), new("weapon_7") };
    private readonly BoneAttachment3D?[] _weaponAttachments = new BoneAttachment3D?[2];
    private readonly List<WeaponBox>[] _weaponBoxes = { new(), new() };
    private readonly List<WeaponBox> _activeBoxes = new();
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] _oldSegs = new (Vector3, Vector3, float, float, float)[20];
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] _stepSegs = new (Vector3, Vector3, float, float, float)[20];
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] _priorSegs = new (Vector3, Vector3, float, float, float)[20];
    private readonly Aabb[] _segmentBounds = new Aabb[20];
    private readonly Aabb[] _currentBounds = new Aabb[20];
    private readonly Vector3[] _segmentForward = new Vector3[20];
    private readonly int[] _segmentBones = new int[20];
    private readonly Vector3[] _oldForward = new Vector3[20];
    private readonly Vector3[] _stepForward = new Vector3[20];
    private Vector3[] _contactNormal = Array.Empty<Vector3>();
    private Vector3[] _contactVelocity = Array.Empty<Vector3>();
    private readonly float[] _limbRadius = new float[8];
    private readonly float[] _limbTipRadius = new float[8];
    private readonly float[] _limbWidth = new float[8];
    private Aabb[] _rowBounds = Array.Empty<Aabb>();
    private int[] _rowLinkStart = Array.Empty<int>();
    private int _segCount;
    private bool _collidersPrimed;
    private float _collisionStep;

    private readonly List<WeaponBox> _boneBoxes = new();

    private sealed class WeaponBox
    {
        public Transform3D Local, Current, Previous, Step, Prior, Inverse, PriorInverse, RenderInverse;
        public Vector3 Half;
        public int SliceAxis, Bone;
        public Aabb Bounds, RenderBounds;
    }

    private void FitWeapon(BoneAttachment3D attachment, List<WeaponBox> boxes)
    {
        boxes.Clear();
        Collect(attachment, Transform3D.Identity);
        void Collect(Node parent, Transform3D transform)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is FxInstance or FxWeaponGlow or FxMesh or GpuParticles3D or ItemShineDriver
                    or WeaponTrail or Node3D { TopLevel: true }) continue;
                var local = child is Node3D nd ? transform * nd.Transform : transform;
                if (child is MeshInstance3D { Mesh: { } mesh, Visible: true })
                {
                    var vertices = new List<Vector3>();
                    var triangles = new List<int>();
                    for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                    {
                        using var arrays = mesh.SurfaceGetArrays(surface);
                        var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                        var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                        int offset = vertices.Count;
                        vertices.AddRange(positions);
                        if (indices.Length == 0)
                            for (int i = 0; i < positions.Length; i++) triangles.Add(offset + i);
                        else foreach (int i in indices) triangles.Add(offset + i);
                    }
                    if (vertices.Count > 0) FitBoxes(vertices, triangles, local, boxes);
                }
                Collect(child, local);
            }
        }
    }

    private static void FitBoxes(List<Vector3> vertices, List<int> triangles, Transform3D local, List<WeaponBox> boxes)
    {
        var min = vertices[0]; var max = min;
        foreach (var v in vertices) { min = min.Min(v); max = max.Max(v); }
        int axis = (int)(max - min).MaxAxisIndex();
        float step = (max[axis] - min[axis]) / WeaponSlices;
        if (step < 1e-5f) return;
        for (int slice = 0; slice < WeaponSlices; slice++)
        {
            float lo = min[axis] + step * slice, hi = lo + step;
            var bmin = Vector3.One * float.PositiveInfinity;
            var bmax = Vector3.One * float.NegativeInfinity;
            void Include(Vector3 v) { bmin = bmin.Min(v); bmax = bmax.Max(v); }
            foreach (var v in vertices)
                if (v[axis] >= lo - 1e-6f && v[axis] <= hi + 1e-6f) Include(v);
            for (int i = 0; i + 2 < triangles.Count; i += 3)
                for (int edge = 0; edge < 3; edge++)
                {
                    var a = vertices[triangles[i + edge]];
                    var b = vertices[triangles[i + (edge + 1) % 3]];
                    float span = b[axis] - a[axis];
                    if (Mathf.Abs(span) < 1e-7f) continue;
                    float t0 = (lo - a[axis]) / span, t1 = (hi - a[axis]) / span;
                    if (t0 >= 0 && t0 <= 1) Include(a.Lerp(b, t0));
                    if (t1 >= 0 && t1 <= 1) Include(a.Lerp(b, t1));
                }
            if (!bmin.IsFinite()) continue;
            var scale = local.Basis.Scale.Abs();
            var padding = new Vector3(0.025f / Mathf.Max(scale.X, 0.01f),
                0.025f / Mathf.Max(scale.Y, 0.01f), 0.025f / Mathf.Max(scale.Z, 0.01f));
            boxes.Add(new WeaponBox
            {
                Local = local * new Transform3D(Basis.Identity, (bmin + bmax) * 0.5f),
                Half = (bmax - bmin) * 0.5f + padding,
                SliceAxis = axis,
            });
        }
    }

    private void UpdateColliders()
    {
        UpdateCollar();
        _segCount = BodySegments(_segs);
        for (int i = 0; i < _segCount; i++)
        {
            int bone = _segmentBones[i];
            var basis = bone < 0 ? _skel.GlobalTransform.Basis * _restBasis
                : _skel.GlobalTransform.Basis * _skel.GetBoneGlobalPose(bone).Basis
                    * _skel.GetBoneGlobalRest(bone).Basis.Inverse() * _restBasis;
            var axis = (_segs[i].B - _segs[i].A).Normalized();
            var forward = basis.Z - axis * basis.Z.Dot(axis);
            _segmentForward[i] = forward.LengthSquared() > 1e-8f ? forward.Normalized() : _bodyFwd;
        }
        for (int i = 0; i < _segCount; i++)
        {
            var seg = _segs[i];
            float radius = Mathf.Max(seg.Ra, seg.Rb) * seg.Aspect;
            var min = seg.A.Min(seg.B) - Vector3.One * radius;
            var max = seg.A.Max(seg.B) + Vector3.One * radius;
            _currentBounds[i] = new Aabb(min, max - min);
        }
        if (_contactNormal.Length != _pos.Length)
        {
            _contactNormal = new Vector3[_pos.Length];
            _contactVelocity = new Vector3[_pos.Length];
        }
        _activeBoxes.Clear();
        for (int hand = 0; hand < WeaponPaths.Length; hand++)
        {
            var attachment = _skel.GetNodeOrNull<BoneAttachment3D>(WeaponPaths[hand]);
            bool changed = attachment != _weaponAttachments[hand];
            if (changed)
            {
                _weaponAttachments[hand] = attachment;
                _weaponBoxes[hand].Clear();
                if (attachment != null) FitWeapon(attachment, _weaponBoxes[hand]);
            }
            if (attachment == null || attachment.BoneIdx < 0 || !attachment.IsVisibleInTree()) continue;
            var transform = _skel.GlobalTransform * _skel.GetBoneGlobalPose(attachment.BoneIdx);
            foreach (var box in _weaponBoxes[hand])
            {
                box.Current = transform * box.Local;
                box.RenderInverse = box.Current.AffineInverse();
                box.RenderBounds = box.Current * new Aabb(-box.Half, box.Half * 2);
                if (changed || !_collidersPrimed) box.Previous = box.Step = box.Prior = box.Current;
                _activeBoxes.Add(box);
            }
        }

        foreach (var box in _boneBoxes)
        {
            box.Current = _skel.GlobalTransform * _skel.GetBoneGlobalPose(box.Bone) * box.Local;
            box.RenderInverse = box.Current.AffineInverse();
            box.RenderBounds = box.Current * new Aabb(-box.Half, box.Half * 2);
            if (!_collidersPrimed) box.Previous = box.Step = box.Prior = box.Current;
            _activeBoxes.Add(box);
        }
        if (!_collidersPrimed)
        {
            Array.Copy(_segs, _oldSegs, _segCount);
            Array.Copy(_segs, _stepSegs, _segCount);
            Array.Copy(_segmentForward, _oldForward, _segCount);
            _collidersPrimed = true;
        }
    }

    private void CommitColliders()
    {
        Array.Copy(_segs, _oldSegs, _segCount);
        Array.Copy(_segmentForward, _oldForward, _segCount);
        foreach (var box in _activeBoxes) box.Previous = box.Current;
    }

    private void PrepareStepColliders(float blend, float h)
    {
        _collisionStep = h;
        for (int i = 0; i < _segCount; i++)
        {
            _priorSegs[i] = _stepSegs[i];
            _stepForward[i] = _oldForward[i].Lerp(_segmentForward[i], blend).Normalized();
            var old = _oldSegs[i]; var current = _segs[i];
            var step = (old.A.Lerp(current.A, blend), old.B.Lerp(current.B, blend),
                current.Ra, current.Rb, current.Aspect);
            _stepSegs[i] = step;
            float radius = Mathf.Max(current.Ra, current.Rb) * current.Aspect;
            var min = step.Item1.Min(step.Item2).Min(_priorSegs[i].A).Min(_priorSegs[i].B) - Vector3.One * radius;
            var max = step.Item1.Max(step.Item2).Max(_priorSegs[i].A).Max(_priorSegs[i].B) + Vector3.One * radius;
            _segmentBounds[i] = new Aabb(min, max - min);
        }
        foreach (var box in _activeBoxes)
        {
            box.Prior = box.Step;
            box.PriorInverse = box.Prior.AffineInverse();
            box.Step = box.Previous.InterpolateWith(box.Current, blend);
            box.Inverse = box.Step.AffineInverse();
            var localBounds = new Aabb(-box.Half, box.Half * 2);
            box.Bounds = (box.Step * localBounds).Merge(box.Prior * localBounds);
        }
    }

    private void CollideParticles(bool swept)
    {
        float floor = _bodyRoot.GlobalPosition.Y + 0.015f;
        for (int i = _nc; i < _pred.Length; i++)
        {
            var p = _pred[i];
            for (int s = 0; s < _segCount; s++)
            {
                if (!_segmentBounds[s].HasPoint(p) && (!swept || !_segmentBounds[s].IntersectsSegment(_pos[i], p))) continue;
                var seg = _stepSegs[s]; var old = _priorSegs[s];
                var forward = _stepForward[s];
                var ab = seg.B - seg.A;
                var warpedAxis = ab + forward * (ab.Dot(forward) * (seg.Aspect - 1));
                var fromA = p - seg.A;
                fromA += forward * (fromA.Dot(forward) * (seg.Aspect - 1));
                float length2 = warpedAxis.LengthSquared();
                float t = length2 > 1e-8f ? Mathf.Clamp(fromA.Dot(warpedAxis) / length2, 0, 1) : 0;
                var center = seg.A + ab * t;
                float radius = Mathf.Lerp(seg.Ra, seg.Rb, t) * seg.Aspect;
                var delta = p - center;
                var warped = delta + forward * (delta.Dot(forward) * (seg.Aspect - 1));
                float distance2 = warped.LengthSquared();
                Vector3 direction;
                if (distance2 < radius * radius)
                {
                    var lateral = warped - forward * warped.Dot(forward);
                    var behind = lateral - forward * Mathf.Sqrt(Mathf.Max(radius * radius - lateral.LengthSquared(), 0));
                    direction = behind / radius;
                }
                else
                {
                    if (!swept) continue;
                    var previous = _pos[i] - old.A.Lerp(old.B, t);
                    previous += forward * (previous.Dot(forward) * (seg.Aspect - 1));
                    if (!SweepSphere(previous, warped, radius, out direction)) continue;
                }
                var offset = direction * radius;
                offset -= forward * (offset.Dot(forward) * (1 - 1 / seg.Aspect));
                p = center + offset;
                _contact[i] = true;
                _contactNormal[i] = (direction + forward * (direction.Dot(forward) * (seg.Aspect - 1))).Normalized();
                _contactVelocity[i] = (center - old.A.Lerp(old.B, t)) / _collisionStep;
            }
            foreach (var box in _activeBoxes)
            {
                if (!box.Bounds.HasPoint(p) && (!swept || !box.Bounds.IntersectsSegment(_pos[i], p))) continue;
                var local = box.Inverse * p;
                var before = swept ? box.PriorInverse * _pos[i] : local;
                if (!PushBox(ref local, before, box.Half, box.SliceAxis, swept, out var normal)) continue;
                p = box.Step * local;
                _contact[i] = true;
                _contactNormal[i] = (box.Inverse.Basis.Transposed() * normal).Normalized();
                _contactVelocity[i] = (p - box.Prior * local) / _collisionStep;
            }
            if (p.Y < floor)
            {
                p.Y = floor;
                _contact[i] = true;
                _contactNormal[i] = Vector3.Up;
                _contactVelocity[i] = Vector3.Zero;
            }
            CollideCollar(ref p);
            _pred[i] = p;
        }
        CollideWeaponEdges();
    }

    private static bool SweepSphere(Vector3 from, Vector3 to, float radius, out Vector3 normal)
    {
        normal = Vector3.Zero;
        var delta = to - from;
        float a = delta.LengthSquared(), b = from.Dot(delta), c = from.LengthSquared() - radius * radius;
        float discriminant = b * b - a * c;
        if (c < 0 || a < 1e-10f || b >= 0 || discriminant < 0) return false;
        float t = (-b - Mathf.Sqrt(discriminant)) / a;
        if (t < 0 || t > 1) return false;
        normal = (from + delta * t).Normalized();
        return true;
    }

    private static bool BoxEntry(Vector3 from, Vector3 to, Vector3 half, out float entry, out Vector3 normal)
    {
        entry = 0; float exit = 1;
        normal = Vector3.Zero;
        var delta = to - from;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(delta[axis]) < 1e-8f)
            {
                if (Mathf.Abs(from[axis]) > half[axis]) return false;
                continue;
            }
            float a = (-half[axis] - from[axis]) / delta[axis];
            float b = (half[axis] - from[axis]) / delta[axis];
            float near = Mathf.Min(a, b), far = Mathf.Max(a, b);
            if (near > entry)
            {
                entry = near; normal = Vector3.Zero;
                normal[axis] = delta[axis] > 0 ? -1 : 1;
            }
            exit = Mathf.Min(exit, far);
            if (entry > exit) return false;
        }
        return exit >= 0 && entry <= 1;
    }

    private static bool PushBox(ref Vector3 p, Vector3 previous, Vector3 half, int sliceAxis, bool swept, out Vector3 normal)
    {
        normal = Vector3.Zero;
        if (swept && BoxEntry(previous, p, half, out float entry, out normal) && normal != Vector3.Zero)
        {
            int axis = (int)normal.Abs().MaxAxisIndex();
            p[axis] = normal[axis] * (half[axis] + 0.001f);
            return true;
        }
        var depth = half - p.Abs();
        if (depth.X < 0 || depth.Y < 0 || depth.Z < 0) return false;
        if (sliceAxis >= 0) depth[sliceAxis] = float.PositiveInfinity;
        int face = (int)depth.MinAxisIndex();
        normal = Vector3.Zero;
        normal[face] = p[face] < 0 ? -1 : 1;
        p[face] = normal[face] * (half[face] + 0.001f);
        return true;
    }

    private void CollideWeaponEdges()
    {
        for (int row = 0; row < _nr; row++)
        {
            var min = _pred[row * _nc]; var max = min;
            int end = Mathf.Min((row + 2) * _nc, _pred.Length);
            for (int i = row * _nc + 1; i < end; i++)
            { min = min.Min(_pred[i]); max = max.Max(_pred[i]); }
            _rowBounds[row] = new Aabb(min, max - min).Grow(0.005f);
        }
        foreach (var box in _activeBoxes)
            for (int row = 0; row < _nr; row++)
            {
                if (!box.Bounds.Intersects(_rowBounds[row])) continue;
                for (int index = _rowLinkStart[row]; index < _rowLinkStart[row + 1]; index++)
                {
                    ref readonly var link = ref _links[index];
                    if (link.Compliance == BendCompliance) continue;
                    var a = _pred[link.A]; var b = _pred[link.B];
                    if (!box.Bounds.IntersectsSegment(a, b)) continue;
                    var la = box.Inverse * a; var lb = box.Inverse * b;
                    if (!BoxEntry(la, lb, box.Half, out float enter, out _)
                        || !BoxEntry(lb, la, box.Half, out float leave, out _)) continue;
                    float t = Mathf.Clamp((enter + 1 - leave) * 0.5f, 0.05f, 0.95f);
                    var contact = la.Lerp(lb, t); var pushed = contact;
                    if (!PushBox(ref pushed, contact, box.Half, box.SliceAxis, false, out var normal)) continue;
                    float wa = _w[link.A] * (1 - t), wb = _w[link.B] * t;
                    float weight = wa * (1 - t) + wb * t;
                    if (weight < 1e-8f) continue;
                    var correction = box.Step.Basis * (pushed - contact) / weight;
                    _pred[link.A] += correction * wa;
                    _pred[link.B] += correction * wb;
                }
            }
    }

    private Aabb[] _surfaceEdgeBounds = Array.Empty<Aabb>();

    private bool CollideSurfaceEdges()
    {
        for (int row = 0; row < _surfaceRows - 1; row++)
        {
            var min = _surfacePos[row * _surfaceCols]; var max = min;
            for (int i = row * _surfaceCols + 1; i < (row + 2) * _surfaceCols; i++)
            { min = min.Min(_surfacePos[i]); max = max.Max(_surfacePos[i]); }
            _surfaceEdgeBounds[row] = new Aabb(min, max - min).Grow(0.005f);
        }
        bool moved = false;
        foreach (var box in _activeBoxes)
            for (int row = 0; row < _surfaceRows - 1; row++)
            {
                if (!box.RenderBounds.Intersects(_surfaceEdgeBounds[row])) continue;
                for (int col = 0; col < _surfaceCols; col++)
                {
                    int a = row * _surfaceCols + col;
                    Edge(a, a + _surfaceCols, box);
                    if (col == _surfaceCols - 1) continue;
                    Edge(a, a + 1, box);
                    Edge(a + 1, a + _surfaceCols, box);
                }
            }
        return moved;
        void Edge(int a, int b, WeaponBox box)
        {
            var from = _surfacePos[a]; var to = _surfacePos[b];
            if (!box.RenderBounds.IntersectsSegment(from, to)) return;
            var la = box.RenderInverse * from; var lb = box.RenderInverse * to;
            if (!BoxEntry(la, lb, box.Half, out float enter, out _)
                || !BoxEntry(lb, la, box.Half, out float leave, out _)) return;
            float t = Mathf.Clamp((enter + 1 - leave) * 0.5f, 0.05f, 0.95f);
            var contact = la.Lerp(lb, t); var pushed = contact;
            if (!PushBox(ref pushed, contact, box.Half, box.SliceAxis, false, out _)) return;
            float wa = a < _surfaceCols ? 0 : 1 - t;
            float wb = b < _surfaceCols ? 0 : t;
            float weight = wa * (1 - t) + wb * t;
            if (weight < 1e-8f) return;
            var correction = box.Current.Basis * (pushed - contact) / weight;
            _surfacePos[a] += correction * wa;
            _surfacePos[b] += correction * wb;
            moved = true;
        }
    }

    private void CollideRender()
    {
        _bodyFwd = Flat(_lastFrameAnchor.Basis.Z);
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            for (int i = _surfaceCols; i < _surfacePos.Length; i++)
            {
                var p = _surfacePos[i];
                for (int s = 0; s < _segCount; s++)
                {
                    if (!_currentBounds[s].HasPoint(p)) continue;
                    var seg = _segs[s]; var ab = seg.B - seg.A;
                    var forward = _segmentForward[s];
                    float length2 = ab.LengthSquared();
                    float t = length2 > 1e-8f ? Mathf.Clamp((p - seg.A).Dot(ab) / length2, 0, 1) : 0;
                    var center = seg.A + ab * t;
                    float radius = Mathf.Lerp(seg.Ra, seg.Rb, t) * seg.Aspect;
                    var d = p - center;
                    d += forward * (d.Dot(forward) * (seg.Aspect - 1));
                    if (d.LengthSquared() >= radius * radius) continue;
                    var lateral = d - forward * d.Dot(forward);
                    d = lateral - forward * Mathf.Sqrt(Mathf.Max(radius * radius - lateral.LengthSquared(), 0));
                    p = center + d - forward * (d.Dot(forward) * (1 - 1 / seg.Aspect));
                }
                foreach (var box in _activeBoxes)
                {
                    if (!box.RenderBounds.HasPoint(p)) continue;
                    var local = box.RenderInverse * p;
                    if (PushBox(ref local, local, box.Half, box.SliceAxis, false, out _)) p = box.Current * local;
                }
                CollideCollar(ref p);
                moved |= p.DistanceSquaredTo(_surfacePos[i]) > 1e-8f;
                _surfacePos[i] = p;
            }
            if (pass < 3) moved |= CollideSurfaceEdges();
            if (!moved) break;
        }
    }

    private void ResolveCapsuleRadii(float neckY)
    {
        float At(int bone, float fallbackT)
        {
            float t = fallbackT;
            if (bone >= 0 && neckY > 0.01f)
                t = Mathf.Clamp((neckY - _skel.GetBoneGlobalRest(bone).Origin.Y) / neckY, 0f, 1f);
            float step = 1f / (ProfileBins - 1);
            return (Mathf.Max(BodyRadiusAt(t),
                    Mathf.Max(BodyRadiusAt(t - step), BodyRadiusAt(t + step))) + CollideInflate) * _rigScale;
        }
        _inflate = CollideInflate * _rigScale;
        _rNeck = (CollarRadius + CollideInflate) * _rigScale;
        _rChest = At(_rideBone, 0.12f);
        _rHips = At(_hipBone, 0.46f);
    }

    private int BodySegments((Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] into)
    {
        if (_hipBone < 0 || _neckBone < 0) return 0;
        var st = _skel.GlobalTransform;
        var neck = st * _skel.GetBoneGlobalPose(_neckBone).Origin;
        var hips = st * _skel.GetBoneGlobalPose(_hipBone).Origin;
        int n = 0;
        void Add(Vector3 a, Vector3 b, float ra, float rb, float aspect, int bone)
        {
            _segmentBones[n] = bone;
            into[n++] = (a, b, ra, rb, aspect);
        }
        Vector3 P(int bone) => st * _skel.GetBoneGlobalPose(bone).Origin;

        var chest = neck.Lerp(hips, 0.35f);
        Add(neck, chest, _rNeck, _rChest, _aspect, _rideBone);
        Add(chest, hips, _rChest, _rHips, _aspect, _rideBone);

        float rThigh = ThighRadius * _rigScale, rCalf = CalfRadius * _rigScale;
        float rUp = UpperArmRadius * _rigScale, rFore = ForeArmRadius * _rigScale;
        if (_upLegL >= 0 && _loLegL >= 0) Add(P(_upLegL), P(_loLegL), Mathf.Max(rThigh, _limbRadius[0]), Mathf.Max(rCalf, _limbTipRadius[0]), LimbAspect(0), _upLegL);
        if (_loLegL >= 0 && _ftLegL >= 0) Add(P(_loLegL), P(_ftLegL), Mathf.Max(rCalf, _limbRadius[1]), Mathf.Max(rCalf, _limbTipRadius[1]), LimbAspect(1), _loLegL);
        if (_upLegR >= 0 && _loLegR >= 0) Add(P(_upLegR), P(_loLegR), Mathf.Max(rThigh, _limbRadius[2]), Mathf.Max(rCalf, _limbTipRadius[2]), LimbAspect(2), _upLegR);
        if (_loLegR >= 0 && _ftLegR >= 0) Add(P(_loLegR), P(_ftLegR), Mathf.Max(rCalf, _limbRadius[3]), Mathf.Max(rCalf, _limbTipRadius[3]), LimbAspect(3), _loLegR);
        float rHand = HandRadius * _rigScale;
        if (_upArmL >= 0 && _loArmL >= 0) Add(P(_upArmL), P(_loArmL), Mathf.Max(rUp, _limbRadius[4]), Mathf.Max(rFore, _limbTipRadius[4]), LimbAspect(4), _upArmL);
        if (_loArmL >= 0 && _handL >= 0) Add(P(_loArmL), P(_handL), Mathf.Max(rFore, _limbRadius[5]), Mathf.Max(rHand, _limbTipRadius[5]), LimbAspect(5), _loArmL);
        if (_upArmR >= 0 && _loArmR >= 0) Add(P(_upArmR), P(_loArmR), Mathf.Max(rUp, _limbRadius[6]), Mathf.Max(rFore, _limbTipRadius[6]), LimbAspect(6), _upArmR);
        if (_loArmR >= 0 && _handR >= 0) Add(P(_loArmR), P(_handR), Mathf.Max(rFore, _limbRadius[7]), Mathf.Max(rHand, _limbTipRadius[7]), LimbAspect(7), _loArmR);
        if (_handL >= 0) Add(P(_handL), P(_handL), rHand, rHand, 1f, _handL);
        if (_handR >= 0) Add(P(_handR), P(_handR), rHand, rHand, 1f, _handR);

        var scale = st.Basis.Scale.Abs();
        float worldScale = Mathf.Max(scale.X, Mathf.Max(scale.Y, scale.Z));
        for (int i = 0; i < n; i++)
        {
            into[i].Ra *= worldScale;
            into[i].Rb *= worldScale;
        }
        return n;
    }

    private float LimbAspect(int limb)
        => Mathf.Clamp(_limbWidth[limb] / Mathf.Max(0.06f * _rigScale,
            Mathf.Max(_limbRadius[limb], _limbTipRadius[limb])), 1, 3);

    private float BodyRadiusAt(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        if (_fitted != null)
        {
            float f = t * (ProfileBins - 1);
            int i = Mathf.Clamp((int)f, 0, ProfileBins - 2);
            return Mathf.Lerp(_fitted[i], _fitted[i + 1], f - i);
        }
        for (int i = 1; i < BodyProfile.Length; i++)
        {
            var (t1, r1) = BodyProfile[i];
            if (t > t1) continue;
            var (t0, r0) = BodyProfile[i - 1];
            return Mathf.Lerp(r0, r1, t1 - t0 < 1e-6f ? 0f : (t - t0) / (t1 - t0));
        }
        return BodyProfile[^1].R;
    }


    public int ColliderSegments() => _segCount + _activeBoxes.Count;

    public int WeaponColliderCount(int hand) => _weaponBoxes[hand].Count;

    public float DeviationNow()
    {
        var anchor = AnchorWorld();
        float worst = 0;
        for (int i = _nc; i < _pos.Length; i++)
            worst = Mathf.Max(worst, _pos[i].DistanceTo(anchor * _rest[i]));
        return worst;
    }

    public float PenetrationDepth()
    {
        float worst = 0;
        for (int s = 0; s < _segCount; s++)
        {
            var seg = _segs[s]; var ab = seg.B - seg.A;
            var forward = _segmentForward[s];
            float length2 = ab.LengthSquared();
            for (int i = _surfaceCols; i < _surfacePos.Length; i++)
            {
                float t = length2 > 1e-8f ? Mathf.Clamp((_surfacePos[i] - seg.A).Dot(ab) / length2, 0, 1) : 0;
                var d = _surfacePos[i] - seg.A - ab * t;
                d += forward * (d.Dot(forward) * (seg.Aspect - 1));
                float depth = Mathf.Lerp(seg.Ra, seg.Rb, t) - _inflate - d.Length() / seg.Aspect;
                worst = Mathf.Max(worst, depth);
            }
        }
        foreach (var box in _activeBoxes)
        {
            var inverse = box.Current.AffineInverse();
            for (int i = _surfaceCols; i < _surfacePos.Length; i++)
            {
                var p = _surfacePos[i];
                var depth = box.Half - (inverse * p).Abs();
                worst = Mathf.Max(worst, Mathf.Min(depth.X, Mathf.Min(depth.Y, depth.Z)) - 0.025f);
            }
        }
        return worst;
    }

    private void DrawDebugOverlay()
    {
        if (_dbg == null)
        {
            _dbg = new MeshInstance3D
            {
                Mesh = new ImmediateMesh(), TopLevel = true,
                CastShadow = ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true, NoDepthTest = true,
                },
            };
            AddChild(_dbg);
        }
        _dbg.GlobalTransform = Transform3D.Identity;
        _dbg.Visible = true;
        var mesh = (ImmediateMesh)_dbg.Mesh;
        mesh.ClearSurfaces();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        void Line(Vector3 a, Vector3 b, Color color)
        {
            mesh.SurfaceSetColor(color); mesh.SurfaceAddVertex(a); mesh.SurfaceAddVertex(b);
        }
        for (int i = 0; i < _segCount; i++)
        {
            var seg = _segs[i];
            Line(seg.A, seg.B, Colors.Green);
            for (int j = 0; j < 16; j++)
            {
                float a = j * Mathf.Tau / 16, b = (j + 1) * Mathf.Tau / 16;
                var forward = _segmentForward[i];
                var axis = seg.B - seg.A;
                if (axis.LengthSquared() < 1e-8f) axis = Vector3.Up;
                var right = axis.Normalized().Cross(forward).Normalized();
                var da = right * (Mathf.Cos(a) * seg.Aspect) + forward * Mathf.Sin(a);
                var db = right * (Mathf.Cos(b) * seg.Aspect) + forward * Mathf.Sin(b);
                Line(seg.A + da * seg.Ra, seg.A + db * seg.Ra, Colors.Green);
                Line(seg.B + da * seg.Rb, seg.B + db * seg.Rb, Colors.Green);
            }
        }
        foreach (var box in _activeBoxes)
            for (int corner = 0; corner < 8; corner++)
                for (int axis = 0; axis < 3; axis++)
                {
                    if ((corner & (1 << axis)) != 0) continue;
                    var a = new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1) * box.Half;
                    var b = a; b[axis] *= -1;
                    Line(box.Current * a, box.Current * b, Colors.Yellow);
                }
        foreach (var link in _links)
            if (link.Compliance == StretchCompliance)
                Line(_renderPos[link.A], _renderPos[link.B], Colors.Cyan);
        mesh.SurfaceEnd();
    }
}
