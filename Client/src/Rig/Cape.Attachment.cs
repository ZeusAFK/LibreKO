using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private readonly record struct PinInfluence(int Bone, Vector3 Point, float Weight);
    private readonly record struct AttachmentVertex(Vector3 Point, PinInfluence[] Influences);
    private int _attachmentFallbacks;
    internal int AttachmentFallbacks => _attachmentFallbacks;
    private PinInfluence[][] _pinBindings = Array.Empty<PinInfluence[]>();
    private Vector3[] _pins = Array.Empty<Vector3>(), _oldPins = Array.Empty<Vector3>(), _stepPins = Array.Empty<Vector3>();

    private void FitAttachment(Node root, Vector3 neck)
    {
        var vertices = new List<AttachmentVertex>();
        var triangles = new List<int>();
        var frame = new Transform3D(_restBasis, neck);
        var toFrame = frame.AffineInverse() * _skel.GlobalTransform.AffineInverse();
        void Collect(Node node)
        {
            if (node is Cape or BoneAttachment3D or FxInstance or FxWeaponGlow or ItemShineDriver) return;
            if (node is MeshInstance3D { Mesh: { } mesh, Visible: true } instance)
            {
                var transform = toFrame * instance.GlobalTransform;
                var skin = instance.Skin;
                for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                {
                    using var arrays = mesh.SurfaceGetArrays(surface);
                    var positions = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
                    var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
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
                                int bone = name.IsEmpty ? skin.GetBindBone(bind) : _skel.FindBone(name);
                                if (bone < 0) continue;
                                binding.Add(new PinInfluence(bone, skin.GetBindPose(bind) * positions[i], weights[at]));
                            }
                        var restPoint = transform * positions[i];
                        if (binding.Count > 0)
                        {
                            restPoint = Vector3.Zero;
                            foreach (var influence in binding)
                                restPoint += (_skel.GetBoneGlobalRest(influence.Bone) * influence.Point) * influence.Weight;
                            restPoint = frame.AffineInverse() * restPoint;
                        }
                        vertices.Add(new AttachmentVertex(restPoint, binding.ToArray()));
                    }
                    if (indices.Length > 0) foreach (int index in indices) triangles.Add(start + index);
                    else for (int i = 0; i < positions.Length; i++) triangles.Add(start + i);
                }
            }
            foreach (var child in node.GetChildren()) Collect(child);
        }
        Collect(root);
        FitCollar(vertices, triangles);
        FitFeet(vertices, frame);
        _pinBindings = new PinInfluence[_nc][];
        _pins = new Vector3[_nc]; _oldPins = new Vector3[_nc]; _stepPins = new Vector3[_nc];
        _attachmentFallbacks = 0;
        float width = 0.20f * _rigScale;
        if (_upArmL >= 0 && _upArmR >= 0)
            width = _skel.GetBoneGlobalRest(_upArmL).Origin.DistanceTo(_skel.GetBoneGlobalRest(_upArmR).Origin) * 0.43f;
        for (int col = 0; col < _nc; col++)
        {
            float side = 1 - 2 * col / (_nc - 1f);
            var point = new Vector3(side * width, (0.025f - 0.045f * Mathf.Abs(side)) * _rigScale, -0.1f * _rigScale);
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
                if (z <= 0.02f * _rigScale && z >= -0.35f * _rigScale && z < back)
                { back = z; best = i; bestU = u; bestV = v; }
            }
            var bindings = new List<PinInfluence>();
            if (best >= 0)
            {
                point.Z = back - 0.008f * _rigScale;
                for (int vertex = 0; vertex < 3; vertex++)
                {
                    float weight = vertex == 0 ? 1 - bestU - bestV : vertex == 1 ? bestU : bestV;
                    foreach (var influence in vertices[triangles[best + vertex]].Influences)
                    {
                        var rest = _skel.GetBoneGlobalRest(influence.Bone);
                        var clearance = rest.Basis.Inverse() * (-_restBasis.Z * 0.008f * _rigScale);
                        bindings.Add(influence with { Point = influence.Point + clearance, Weight = influence.Weight * weight });
                    }
                }
            }
            if (bindings.Count == 0)
            {
                _attachmentFallbacks++;
                bindings.Add(new PinInfluence(_rideBone, _skel.GetBoneGlobalRest(_rideBone).AffineInverse() * (frame * point), 1));
            }
            _pinBindings[col] = bindings.ToArray();
            var offset = point - _rest[col];
            for (int row = 0; row < _nr; row++)
            {
                int i = row * _nc + col;
                float weight = 1 - Mathf.SmoothStep(0, 0.35f * _rigScale, -_rest[i].Y);
                _rest[i] += offset * weight;
            }
        }
    }

    private void FitFeet(List<AttachmentVertex> vertices, Transform3D frame)
    {
        _boneBoxes.Clear();
        foreach (int bone in new[] { _ftLegL, _ftLegR })
        {
            if (bone < 0) continue;
            var rest = _skel.GetBoneGlobalRest(bone);
            var footFrame = new Transform3D(_restBasis, rest.Origin);
            var inverse = footFrame.AffineInverse();
            var min = Vector3.One * float.PositiveInfinity;
            var max = Vector3.One * float.NegativeInfinity;
            foreach (var vertex in vertices)
            {
                float weight = 0;
                foreach (var influence in vertex.Influences)
                    if (influence.Bone == bone) weight += influence.Weight;
                var point = inverse * (frame * vertex.Point);
                if (weight < 0.25f || point.Y > 0.09f * _rigScale) continue;
                min = min.Min(point); max = max.Max(point);
            }
            if (!min.IsFinite()) continue;
            _boneBoxes.Add(new WeaponBox
            {
                Bone = bone, SliceAxis = -1,
                Local = rest.AffineInverse() * footFrame * new Transform3D(Basis.Identity, (min + max) * 0.5f),
                Half = (max - min) * 0.5f + Vector3.One * (0.025f * _rigScale),
            });
        }
    }

    private void UpdatePins()
    {
        var world = _skel.GlobalTransform;
        for (int col = 0; col < _nc; col++)
        {
            var point = Vector3.Zero;
            foreach (var influence in _pinBindings[col])
                point += (_skel.GetBoneGlobalPose(influence.Bone) * influence.Point) * influence.Weight;
            _pins[col] = world * point;
        }
    }
}
