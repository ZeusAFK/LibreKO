using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private const int WeaponSlices = 8;
    private static readonly NodePath[] WeaponPaths = { new("weapon_6"), new("weapon_7") };
    private readonly BoneAttachment3D?[] weaponAttachments = new BoneAttachment3D?[2];
    private readonly List<WeaponBox>[] weaponBoxes = { new(), new() };
    private readonly List<WeaponBox> activeBoxes = new();
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] oldSegs = new (Vector3, Vector3, float, float, float)[20];
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] stepSegs = new (Vector3, Vector3, float, float, float)[20];
    private readonly (Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] priorSegs = new (Vector3, Vector3, float, float, float)[20];
    private readonly Aabb[] segmentBounds = new Aabb[20];
    private readonly Aabb[] currentBounds = new Aabb[20];
    private readonly Vector3[] segmentForward = new Vector3[20];
    private readonly int[] segmentBones = new int[20];
    private readonly Vector3[] oldForward = new Vector3[20];
    private readonly Vector3[] stepForward = new Vector3[20];
    private Vector3[] contactNormal = Array.Empty<Vector3>();
    private Vector3[] contactVelocity = Array.Empty<Vector3>();
    private readonly float[] limbRadius = new float[8];
    private readonly float[] limbTipRadius = new float[8];
    private readonly float[] limbWidth = new float[8];
    private Aabb[] rowBounds = Array.Empty<Aabb>();
    private int[] rowLinkStart = Array.Empty<int>();
    private int segCount;
    private bool collidersPrimed;
    private float collisionStep;

    private readonly List<WeaponBox> boneBoxes = new();

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
                if (child is FxInstance or FxWeaponGlow or FxMesh or FxParticles or GpuParticles3D or ItemShineDriver
                    or WeaponTrail or Node3D { TopLevel: true }) continue;
                var local = child is Node3D nd ? transform * nd.Transform : transform;
                if (child is MeshInstance3D { Mesh: { } sourceMesh, Visible: true })
                {
                    var vertices = new List<Vector3>();
                    var triangles = new List<int>();
                    for (int surface = 0; surface < sourceMesh.GetSurfaceCount(); surface++)
                    {
                        var cached = MeshArraysCache.Get(sourceMesh, surface);
                        var positions = cached.Vertices;
                        var indices = cached.Indices;
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
        segCount = BodySegments(segs);
        for (int i = 0; i < segCount; i++)
        {
            int bone = segmentBones[i];
            var basis = bone < 0 ? skel.GlobalTransform.Basis * restBasis
                : skel.GlobalTransform.Basis * skel.GetBoneGlobalPose(bone).Basis
                    * skel.GetBoneGlobalRest(bone).Basis.Inverse() * restBasis;
            var axis = (segs[i].B - segs[i].A).Normalized();
            var forward = basis.Z - axis * basis.Z.Dot(axis);
            segmentForward[i] = forward.LengthSquared() > 1e-8f ? forward.Normalized() : bodyFwd;
        }
        for (int i = 0; i < segCount; i++)
        {
            var seg = segs[i];
            float radius = Mathf.Max(seg.Ra, seg.Rb) * seg.Aspect;
            var min = seg.A.Min(seg.B) - Vector3.One * radius;
            var max = seg.A.Max(seg.B) + Vector3.One * radius;
            currentBounds[i] = new Aabb(min, max - min);
        }
        if (contactNormal.Length != pos.Length)
        {
            contactNormal = new Vector3[pos.Length];
            contactVelocity = new Vector3[pos.Length];
        }
        activeBoxes.Clear();
        for (int hand = 0; hand < WeaponPaths.Length; hand++)
        {
            var attachment = skel.GetNodeOrNull<BoneAttachment3D>(WeaponPaths[hand]);
            bool changed = attachment != weaponAttachments[hand];
            if (changed)
            {
                weaponAttachments[hand] = attachment;
                weaponBoxes[hand].Clear();
                if (attachment != null) FitWeapon(attachment, weaponBoxes[hand]);
            }
            if (attachment == null || attachment.BoneIdx < 0 || !attachment.IsVisibleInTree()) continue;
            var transform = skel.GlobalTransform * skel.GetBoneGlobalPose(attachment.BoneIdx);
            foreach (var box in weaponBoxes[hand])
            {
                box.Current = transform * box.Local;
                box.RenderInverse = box.Current.AffineInverse();
                box.RenderBounds = box.Current * new Aabb(-box.Half, box.Half * 2);
                if (changed || !collidersPrimed) box.Previous = box.Step = box.Prior = box.Current;
                activeBoxes.Add(box);
            }
        }

        foreach (var box in boneBoxes)
        {
            box.Current = skel.GlobalTransform * skel.GetBoneGlobalPose(box.Bone) * box.Local;
            box.RenderInverse = box.Current.AffineInverse();
            box.RenderBounds = box.Current * new Aabb(-box.Half, box.Half * 2);
            if (!collidersPrimed) box.Previous = box.Step = box.Prior = box.Current;
            activeBoxes.Add(box);
        }
        if (!collidersPrimed)
        {
            Array.Copy(segs, oldSegs, segCount);
            Array.Copy(segs, stepSegs, segCount);
            Array.Copy(segmentForward, oldForward, segCount);
            collidersPrimed = true;
        }
    }

    private void CommitColliders()
    {
        Array.Copy(segs, oldSegs, segCount);
        Array.Copy(segmentForward, oldForward, segCount);
        foreach (var box in activeBoxes) box.Previous = box.Current;
    }

    private void PrepareStepColliders(float blend, float h)
    {
        collisionStep = h;
        for (int i = 0; i < segCount; i++)
        {
            priorSegs[i] = stepSegs[i];
            stepForward[i] = oldForward[i].Lerp(segmentForward[i], blend).Normalized();
            var old = oldSegs[i]; var current = segs[i];
            var step = (old.A.Lerp(current.A, blend), old.B.Lerp(current.B, blend),
                current.Ra, current.Rb, current.Aspect);
            stepSegs[i] = step;
            float radius = Mathf.Max(current.Ra, current.Rb) * current.Aspect;
            var min = step.Item1.Min(step.Item2).Min(priorSegs[i].A).Min(priorSegs[i].B) - Vector3.One * radius;
            var max = step.Item1.Max(step.Item2).Max(priorSegs[i].A).Max(priorSegs[i].B) + Vector3.One * radius;
            segmentBounds[i] = new Aabb(min, max - min);
        }
        foreach (var box in activeBoxes)
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
        float floor = bodyRoot.GlobalPosition.Y + 0.015f;
        for (int i = gridColumns; i < pred.Length; i++)
        {
            var p = pred[i];
            for (int s = 0; s < segCount; s++)
            {
                if (!segmentBounds[s].HasPoint(p) && (!swept || !segmentBounds[s].IntersectsSegment(pos[i], p))) continue;
                var seg = stepSegs[s]; var old = priorSegs[s];
                var forward = stepForward[s];
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
                    var previousPosition = pos[i] - old.A.Lerp(old.B, t);
                    previousPosition += forward * (previousPosition.Dot(forward) * (seg.Aspect - 1));
                    if (!SweepSphere(previousPosition, warped, radius, out direction)) continue;
                }
                var offset = direction * radius;
                offset -= forward * (offset.Dot(forward) * (1 - 1 / seg.Aspect));
                p = center + offset;
                contact[i] = true;
                contactNormal[i] = (direction + forward * (direction.Dot(forward) * (seg.Aspect - 1))).Normalized();
                contactVelocity[i] = (center - old.A.Lerp(old.B, t)) / collisionStep;
            }
            foreach (var box in activeBoxes)
            {
                if (!box.Bounds.HasPoint(p) && (!swept || !box.Bounds.IntersectsSegment(pos[i], p))) continue;
                var local = box.Inverse * p;
                var before = swept ? box.PriorInverse * pos[i] : local;
                if (!PushBox(ref local, before, box.Half, box.SliceAxis, swept, out var normal)) continue;
                p = box.Step * local;
                contact[i] = true;
                contactNormal[i] = (box.Inverse.Basis.Transposed() * normal).Normalized();
                contactVelocity[i] = (p - box.Prior * local) / collisionStep;
            }
            if (p.Y < floor)
            {
                p.Y = floor;
                contact[i] = true;
                contactNormal[i] = Vector3.Up;
                contactVelocity[i] = Vector3.Zero;
            }
            CollideCollar(ref p);
            pred[i] = p;
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

    private static bool PushBox(ref Vector3 p, Vector3 previousPosition, Vector3 half, int sliceAxis, bool swept, out Vector3 normal)
    {
        normal = Vector3.Zero;
        if (swept && BoxEntry(previousPosition, p, half, out float entry, out normal) && normal != Vector3.Zero)
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
        for (int row = 0; row < gridRows; row++)
        {
            var min = pred[row * gridColumns]; var max = min;
            int end = Mathf.Min((row + 2) * gridColumns, pred.Length);
            for (int i = row * gridColumns + 1; i < end; i++)
            { min = min.Min(pred[i]); max = max.Max(pred[i]); }
            rowBounds[row] = new Aabb(min, max - min).Grow(0.005f);
        }
        foreach (var box in activeBoxes)
            for (int row = 0; row < gridRows; row++)
            {
                if (!box.Bounds.Intersects(rowBounds[row])) continue;
                for (int index = rowLinkStart[row]; index < rowLinkStart[row + 1]; index++)
                {
                    ref readonly var link = ref links[index];
                    if (link.Compliance == BendCompliance) continue;
                    var a = pred[link.A]; var b = pred[link.B];
                    if (!box.Bounds.IntersectsSegment(a, b)) continue;
                    var la = box.Inverse * a; var lb = box.Inverse * b;
                    if (!BoxEntry(la, lb, box.Half, out float enter, out _)
                        || !BoxEntry(lb, la, box.Half, out float leave, out _)) continue;
                    float t = Mathf.Clamp((enter + 1 - leave) * 0.5f, 0.05f, 0.95f);
                    var contactPoint = la.Lerp(lb, t); var pushed = contactPoint;
                    if (!PushBox(ref pushed, contactPoint, box.Half, box.SliceAxis, false, out var normal)) continue;
                    float wa = inverseMass[link.A] * (1 - t), wb = inverseMass[link.B] * t;
                    float weight = wa * (1 - t) + wb * t;
                    if (weight < 1e-8f) continue;
                    var correction = box.Step.Basis * (pushed - contactPoint) / weight;
                    pred[link.A] += correction * wa;
                    pred[link.B] += correction * wb;
                }
            }
    }

    private Aabb[] surfaceEdgeBounds = Array.Empty<Aabb>();

    private bool CollideSurfaceEdges()
    {
        for (int row = 0; row < surfaceRows - 1; row++)
        {
            var min = surfacePos[row * surfaceCols]; var max = min;
            for (int i = row * surfaceCols + 1; i < (row + 2) * surfaceCols; i++)
            { min = min.Min(surfacePos[i]); max = max.Max(surfacePos[i]); }
            surfaceEdgeBounds[row] = new Aabb(min, max - min).Grow(0.005f);
        }
        bool moved = false;
        foreach (var box in activeBoxes)
            for (int row = 0; row < surfaceRows - 1; row++)
            {
                if (!box.RenderBounds.Intersects(surfaceEdgeBounds[row])) continue;
                for (int col = 0; col < surfaceCols; col++)
                {
                    int a = row * surfaceCols + col;
                    Edge(a, a + surfaceCols, box);
                    if (col == surfaceCols - 1) continue;
                    Edge(a, a + 1, box);
                    Edge(a + 1, a + surfaceCols, box);
                }
            }
        return moved;
        void Edge(int a, int b, WeaponBox box)
        {
            var from = surfacePos[a]; var to = surfacePos[b];
            if (!box.RenderBounds.IntersectsSegment(from, to)) return;
            var la = box.RenderInverse * from; var lb = box.RenderInverse * to;
            if (!BoxEntry(la, lb, box.Half, out float enter, out _)
                || !BoxEntry(lb, la, box.Half, out float leave, out _)) return;
            float t = Mathf.Clamp((enter + 1 - leave) * 0.5f, 0.05f, 0.95f);
            var contactPoint = la.Lerp(lb, t); var pushed = contactPoint;
            if (!PushBox(ref pushed, contactPoint, box.Half, box.SliceAxis, false, out _)) return;
            float wa = a < surfaceCols ? 0 : 1 - t;
            float wb = b < surfaceCols ? 0 : t;
            float weight = wa * (1 - t) + wb * t;
            if (weight < 1e-8f) return;
            var correction = box.Current.Basis * (pushed - contactPoint) / weight;
            surfacePos[a] += correction * wa;
            surfacePos[b] += correction * wb;
            moved = true;
        }
    }

    private void CollideRender()
    {
        bodyFwd = Flat(lastFrameAnchor.Basis.Z);
        for (int pass = 0; pass < 4; pass++)
        {
            bool moved = false;
            for (int i = surfaceCols; i < surfacePos.Length; i++)
            {
                var p = surfacePos[i];
                for (int s = 0; s < segCount; s++)
                {
                    if (!currentBounds[s].HasPoint(p)) continue;
                    var seg = segs[s]; var ab = seg.B - seg.A;
                    var forward = segmentForward[s];
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
                foreach (var box in activeBoxes)
                {
                    if (!box.RenderBounds.HasPoint(p)) continue;
                    var local = box.RenderInverse * p;
                    if (PushBox(ref local, local, box.Half, box.SliceAxis, false, out _)) p = box.Current * local;
                }
                CollideCollar(ref p);
                moved |= p.DistanceSquaredTo(surfacePos[i]) > 1e-8f;
                surfacePos[i] = p;
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
                t = Mathf.Clamp((neckY - skel.GetBoneGlobalRest(bone).Origin.Y) / neckY, 0f, 1f);
            float step = 1f / (ProfileBins - 1);
            return (Mathf.Max(BodyRadiusAt(t),
                    Mathf.Max(BodyRadiusAt(t - step), BodyRadiusAt(t + step))) + CollideInflate) * rigScale;
        }
        inflate = CollideInflate * rigScale;
        rNeck = (CollarRadius + CollideInflate) * rigScale;
        rChest = At(rideBone, 0.12f);
        rHips = At(hipBone, 0.46f);
    }

    private int BodySegments((Vector3 A, Vector3 B, float Ra, float Rb, float Aspect)[] into)
    {
        if (hipBone < 0 || neckBone < 0) return 0;
        var st = skel.GlobalTransform;
        var neck = st * skel.GetBoneGlobalPose(neckBone).Origin;
        var hips = st * skel.GetBoneGlobalPose(hipBone).Origin;
        int n = 0;
        void Add(Vector3 a, Vector3 b, float ra, float rb, float aspect, int bone)
        {
            segmentBones[n] = bone;
            into[n++] = (a, b, ra, rb, aspect);
        }
        Vector3 P(int bone) => st * skel.GetBoneGlobalPose(bone).Origin;

        var chest = neck.Lerp(hips, 0.35f);
        Add(neck, chest, rNeck, rChest, aspect, rideBone);
        Add(chest, hips, rChest, rHips, aspect, rideBone);

        float rThigh = ThighRadius * rigScale, rCalf = CalfRadius * rigScale;
        float rUp = UpperArmRadius * rigScale, rFore = ForeArmRadius * rigScale;
        if (upLegL >= 0 && loLegL >= 0) Add(P(upLegL), P(loLegL), Mathf.Max(rThigh, limbRadius[0]), Mathf.Max(rCalf, limbTipRadius[0]), LimbAspect(0), upLegL);
        if (loLegL >= 0 && ftLegL >= 0) Add(P(loLegL), P(ftLegL), Mathf.Max(rCalf, limbRadius[1]), Mathf.Max(rCalf, limbTipRadius[1]), LimbAspect(1), loLegL);
        if (upLegR >= 0 && loLegR >= 0) Add(P(upLegR), P(loLegR), Mathf.Max(rThigh, limbRadius[2]), Mathf.Max(rCalf, limbTipRadius[2]), LimbAspect(2), upLegR);
        if (loLegR >= 0 && ftLegR >= 0) Add(P(loLegR), P(ftLegR), Mathf.Max(rCalf, limbRadius[3]), Mathf.Max(rCalf, limbTipRadius[3]), LimbAspect(3), loLegR);
        float rHand = HandRadius * rigScale;
        if (upArmL >= 0 && loArmL >= 0) Add(P(upArmL), P(loArmL), Mathf.Max(rUp, limbRadius[4]), Mathf.Max(rFore, limbTipRadius[4]), LimbAspect(4), upArmL);
        if (loArmL >= 0 && handL >= 0) Add(P(loArmL), P(handL), Mathf.Max(rFore, limbRadius[5]), Mathf.Max(rHand, limbTipRadius[5]), LimbAspect(5), loArmL);
        if (upArmR >= 0 && loArmR >= 0) Add(P(upArmR), P(loArmR), Mathf.Max(rUp, limbRadius[6]), Mathf.Max(rFore, limbTipRadius[6]), LimbAspect(6), upArmR);
        if (loArmR >= 0 && handR >= 0) Add(P(loArmR), P(handR), Mathf.Max(rFore, limbRadius[7]), Mathf.Max(rHand, limbTipRadius[7]), LimbAspect(7), loArmR);
        if (handL >= 0) Add(P(handL), P(handL), rHand, rHand, 1f, handL);
        if (handR >= 0) Add(P(handR), P(handR), rHand, rHand, 1f, handR);

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
        => Mathf.Clamp(limbWidth[limb] / Mathf.Max(0.06f * rigScale,
            Mathf.Max(limbRadius[limb], limbTipRadius[limb])), 1, 3);

    private float BodyRadiusAt(float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        if (fitted != null)
        {
            float f = t * (ProfileBins - 1);
            int i = Mathf.Clamp((int)f, 0, ProfileBins - 2);
            return Mathf.Lerp(fitted[i], fitted[i + 1], f - i);
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


    public int ColliderSegments() => segCount + activeBoxes.Count;

    public int WeaponColliderCount(int hand) => weaponBoxes[hand].Count;

    public float DeviationNow()
    {
        var anchor = AnchorWorld();
        float worst = 0;
        for (int i = gridColumns; i < pos.Length; i++)
            worst = Mathf.Max(worst, pos[i].DistanceTo(anchor * rest[i]));
        return worst;
    }

    public float PenetrationDepth()
    {
        float worst = 0;
        for (int s = 0; s < segCount; s++)
        {
            var seg = segs[s]; var ab = seg.B - seg.A;
            var forward = segmentForward[s];
            float length2 = ab.LengthSquared();
            for (int i = surfaceCols; i < surfacePos.Length; i++)
            {
                float t = length2 > 1e-8f ? Mathf.Clamp((surfacePos[i] - seg.A).Dot(ab) / length2, 0, 1) : 0;
                var d = surfacePos[i] - seg.A - ab * t;
                d += forward * (d.Dot(forward) * (seg.Aspect - 1));
                float depth = Mathf.Lerp(seg.Ra, seg.Rb, t) - inflate - d.Length() / seg.Aspect;
                worst = Mathf.Max(worst, depth);
            }
        }
        foreach (var box in activeBoxes)
        {
            var inverse = box.Current.AffineInverse();
            for (int i = surfaceCols; i < surfacePos.Length; i++)
            {
                var p = surfacePos[i];
                var depth = box.Half - (inverse * p).Abs();
                worst = Mathf.Max(worst, Mathf.Min(depth.X, Mathf.Min(depth.Y, depth.Z)) - 0.025f);
            }
        }
        return worst;
    }

    private void DrawDebugOverlay()
    {
        if (dbg == null)
        {
            dbg = new MeshInstance3D
            {
                Mesh = new ImmediateMesh(), TopLevel = true,
                CastShadow = ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true, NoDepthTest = true,
                },
            };
            AddChild(dbg);
        }
        dbg.GlobalTransform = Transform3D.Identity;
        dbg.Visible = true;
        var debugMesh = (ImmediateMesh)dbg.Mesh;
        debugMesh.ClearSurfaces();
        debugMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        void Line(Vector3 a, Vector3 b, Color color)
        {
            debugMesh.SurfaceSetColor(color); debugMesh.SurfaceAddVertex(a); debugMesh.SurfaceAddVertex(b);
        }
        for (int i = 0; i < segCount; i++)
        {
            var seg = segs[i];
            Line(seg.A, seg.B, Colors.Green);
            for (int j = 0; j < 16; j++)
            {
                float a = j * Mathf.Tau / 16, b = (j + 1) * Mathf.Tau / 16;
                var forward = segmentForward[i];
                var axis = seg.B - seg.A;
                if (axis.LengthSquared() < 1e-8f) axis = Vector3.Up;
                var right = axis.Normalized().Cross(forward).Normalized();
                var da = right * (Mathf.Cos(a) * seg.Aspect) + forward * Mathf.Sin(a);
                var db = right * (Mathf.Cos(b) * seg.Aspect) + forward * Mathf.Sin(b);
                Line(seg.A + da * seg.Ra, seg.A + db * seg.Ra, Colors.Green);
                Line(seg.B + da * seg.Rb, seg.B + db * seg.Rb, Colors.Green);
            }
        }
        foreach (var box in activeBoxes)
            for (int corner = 0; corner < 8; corner++)
                for (int axis = 0; axis < 3; axis++)
                {
                    if ((corner & (1 << axis)) != 0) continue;
                    var a = new Vector3((corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1) * box.Half;
                    var b = a; b[axis] *= -1;
                    Line(box.Current * a, box.Current * b, Colors.Yellow);
                }
        foreach (var link in links)
            if (link.Compliance == StretchCompliance)
                Line(renderPos[link.A], renderPos[link.B], Colors.Cyan);
        debugMesh.SurfaceEnd();
    }
}
