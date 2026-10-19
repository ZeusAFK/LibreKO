using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private readonly record struct PinInfluence(int Bone, Vector3 Point, float Weight);
    private readonly record struct AttachmentVertex(Vector3 Point, PinInfluence[] Influences);
    private int attachmentFallbacks;
    internal int AttachmentFallbacks => attachmentFallbacks;
    private PinInfluence[][] pinBindings = Array.Empty<PinInfluence[]>();
    private Vector3[] pins = Array.Empty<Vector3>(), oldPins = Array.Empty<Vector3>(), stepPins = Array.Empty<Vector3>();

    private const int AttachmentCacheLimit = 512;
    private readonly record struct AttachmentPart(Mesh Mesh, Skin? Skin, Transform3D Transform);
    private sealed record AttachmentSource(List<AttachmentVertex> Vertices, List<int> Triangles);
    private static readonly Dictionary<string, AttachmentSource> AttachmentCache =
        Shutdown.Track(new Dictionary<string, AttachmentSource>());

    private void FitAttachment(Node root, Vector3 neck)
    {
        var frame = new Transform3D(restBasis, neck);
        var toFrame = frame.AffineInverse() * skel.GlobalTransform.AffineInverse();
        var parts = new List<AttachmentPart>();
        void Gather(Node node)
        {
            if (node is Cape or BoneAttachment3D or FxInstance or FxWeaponGlow or ItemShineDriver) return;
            if (node is MeshInstance3D { Mesh: { } sourceMesh, Visible: true } instance)
                parts.Add(new AttachmentPart(sourceMesh, instance.Skin, toFrame * instance.GlobalTransform));
            foreach (var child in node.GetChildren()) Gather(child);
        }
        Gather(root);
        string key = AttachmentKey(parts, frame);
        if (!AttachmentCache.TryGetValue(key, out var source))
        {
            source = CollectAttachment(parts, frame);
            if (AttachmentCache.Count >= AttachmentCacheLimit) AttachmentCache.Clear();
            AttachmentCache[key] = source;
        }
        FitPins(source.Vertices, source.Triangles, frame);
    }

    private string AttachmentKey(List<AttachmentPart> parts, Transform3D frame)
    {
        var key = new System.Text.StringBuilder();
        key.Append(rigScale.ToString("F4")).Append('|').Append(Rounded(frame));
        for (int bone = 0; bone < skel.GetBoneCount(); bone++) key.Append('|').Append(skel.GetBoneName(bone));
        foreach (var part in parts)
            key.Append('#').Append(part.Mesh.GetRid().Id).Append(':').Append(part.Skin?.GetRid().Id ?? 0).Append(':').Append(Rounded(part.Transform));
        return key.ToString();
    }

    private static string Rounded(Transform3D t) =>
        $"{t.Basis.X.X:F3},{t.Basis.X.Y:F3},{t.Basis.X.Z:F3},{t.Basis.Y.X:F3},{t.Basis.Y.Y:F3},{t.Basis.Y.Z:F3},"
        + $"{t.Basis.Z.X:F3},{t.Basis.Z.Y:F3},{t.Basis.Z.Z:F3},{t.Origin.X:F3},{t.Origin.Y:F3},{t.Origin.Z:F3}";

    private AttachmentSource CollectAttachment(List<AttachmentPart> parts, Transform3D frame)
    {
        var vertices = new List<AttachmentVertex>();
        var triangles = new List<int>();
        foreach (var (sourceMesh, skin, transform) in parts)
        {
            for (int surface = 0; surface < sourceMesh.GetSurfaceCount(); surface++)
            {
                var cached = MeshArraysCache.Get(sourceMesh, surface);
                var positions = cached.Vertices;
                var indices = cached.Indices;
                var bones = cached.Bones;
                var weights = cached.Weights;
                int influences = positions.Length > 0 ? bones.Length / positions.Length : 0;
                int start = vertices.Count;
                for (int i = 0; i < positions.Length; i++)
                {
                    var binding = new List<PinInfluence>();
                    if (skin != null)
                        for (int j = 0; j < influences; j++)
                        {
                            int at = i * influences + j;
                            if (weights[at] <= 0) continue;
                            int bind = bones[at];
                            var name = skin.GetBindName(bind);
                            int bone = name.IsEmpty ? skin.GetBindBone(bind) : skel.FindBone(name);
                            if (bone < 0) continue;
                            binding.Add(new PinInfluence(bone, skin.GetBindPose(bind) * positions[i], weights[at]));
                        }
                    var restPoint = transform * positions[i];
                    if (binding.Count > 0)
                    {
                        restPoint = Vector3.Zero;
                        foreach (var influence in binding)
                            restPoint += (skel.GetBoneGlobalRest(influence.Bone) * influence.Point) * influence.Weight;
                        restPoint = frame.AffineInverse() * restPoint;
                    }
                    vertices.Add(new AttachmentVertex(restPoint, binding.ToArray()));
                }
                if (indices.Length > 0) foreach (int index in indices) triangles.Add(start + index);
                else for (int i = 0; i < positions.Length; i++) triangles.Add(start + i);
            }
        }
        return new AttachmentSource(vertices, triangles);
    }

    private void FitPins(List<AttachmentVertex> vertices, List<int> triangles, Transform3D frame)
    {
        FitCollar(vertices, triangles);
        FitFeet(vertices, frame);
        pinBindings = new PinInfluence[gridColumns][];
        pins = new Vector3[gridColumns]; oldPins = new Vector3[gridColumns]; stepPins = new Vector3[gridColumns];
        attachmentFallbacks = 0;
        float width = 0.20f * rigScale;
        if (upArmL >= 0 && upArmR >= 0)
            width = skel.GetBoneGlobalRest(upArmL).Origin.DistanceTo(skel.GetBoneGlobalRest(upArmR).Origin) * 0.43f;
        for (int col = 0; col < gridColumns; col++)
        {
            float side = 1 - 2 * col / (gridColumns - 1f);
            var point = new Vector3(side * width, (0.025f - 0.045f * Mathf.Abs(side)) * rigScale, -0.1f * rigScale);
            float back = float.PositiveInfinity, bestU = 0, bestV = 0;
            int best = -1;
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                var a = vertices[triangles[i]].Point;
                var ab = vertices[triangles[i + 1]].Point - a;
                var ac = vertices[triangles[i + 2]].Point - a;
                float det = ab.X * ac.Y - ab.Y * ac.X;
                if (Mathf.Abs(det) < 1e-9f) continue;
                var d = point - a;
                float u = (d.X * ac.Y - d.Y * ac.X) / det;
                float v = (ab.X * d.Y - ab.Y * d.X) / det;
                if (u < 0 || v < 0 || u + v > 1) continue;
                float z = a.Z + ab.Z * u + ac.Z * v;
                if (z <= 0.02f * rigScale && z >= -0.35f * rigScale && z < back)
                { back = z; best = i; bestU = u; bestV = v; }
            }
            var bindings = new List<PinInfluence>();
            if (best >= 0)
            {
                point.Z = back - 0.008f * rigScale;
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    float weight = vertex == 0 ? 1 - bestU - bestV : vertex == 1 ? bestU : bestV;
                    foreach (var influence in vertices[triangles[best + vertex]].Influences)
                    {
                        var restPose = skel.GetBoneGlobalRest(influence.Bone);
                        var clearance = restPose.Basis.Inverse() * (-restBasis.Z * 0.008f * rigScale);
                        bindings.Add(influence with { Point = influence.Point + clearance, Weight = influence.Weight * weight });
                    }
                }
            }
            if (bindings.Count == 0)
            {
                attachmentFallbacks++;
                bindings.Add(new PinInfluence(rideBone, skel.GetBoneGlobalRest(rideBone).AffineInverse() * (frame * point), 1));
            }
            pinBindings[col] = bindings.ToArray();
            var offset = point - rest[col];
            for (int row = 0; row < gridRows; row++)
            {
                int i = row * gridColumns + col;
                float weight = 1 - Mathf.SmoothStep(0, 0.35f * rigScale, -rest[i].Y);
                rest[i] += offset * weight;
            }
        }
    }

    private void FitFeet(List<AttachmentVertex> vertices, Transform3D frame)
    {
        boneBoxes.Clear();
        foreach (int bone in new[] { ftLegL, ftLegR })
        {
            if (bone < 0) continue;
            var restPose = skel.GetBoneGlobalRest(bone);
            var footFrame = new Transform3D(restBasis, restPose.Origin);
            var inverse = footFrame.AffineInverse();
            var min = Vector3.One * float.PositiveInfinity;
            var max = Vector3.One * float.NegativeInfinity;
            foreach (var vertex in vertices)
            {
                float weight = 0;
                foreach (var influence in vertex.Influences)
                    if (influence.Bone == bone) weight += influence.Weight;
                var point = inverse * (frame * vertex.Point);
                if (weight < 0.25f || point.Y > 0.09f * rigScale) continue;
                min = min.Min(point); max = max.Max(point);
            }
            if (!min.IsFinite()) continue;
            boneBoxes.Add(new WeaponBox
            {
                Bone = bone, SliceAxis = -1,
                Local = restPose.AffineInverse() * footFrame * new Transform3D(Basis.Identity, (min + max) * 0.5f),
                Half = (max - min) * 0.5f + Vector3.One * (0.025f * rigScale),
            });
        }
    }

    private void UpdatePins()
    {
        var world = skel.GlobalTransform;
        for (int col = 0; col < gridColumns; col++)
        {
            var point = Vector3.Zero;
            foreach (var influence in pinBindings[col])
                point += (skel.GetBoneGlobalPose(influence.Bone) * influence.Point) * influence.Weight;
            pins[col] = world * point;
        }
    }
}
