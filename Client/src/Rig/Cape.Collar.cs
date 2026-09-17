using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private const int CollarGridSize = 8;
    private readonly int[] _collarCellCounts = new int[CollarGridSize * CollarGridSize];
    private int[] _collarCells = Array.Empty<int>();
    private Vector2[] _collarU = Array.Empty<Vector2>(), _collarV = Array.Empty<Vector2>();
    private Vector3[] _collarPlanes = Array.Empty<Vector3>();
    private Vector2 _collarGridScale;
    private AttachmentVertex[] _collarVertices = Array.Empty<AttachmentVertex>();
    private Vector3[] _collarPos = Array.Empty<Vector3>();
    private int[] _collarIndices = Array.Empty<int>();
    private Aabb[] _collarBounds = Array.Empty<Aabb>();
    private Transform3D[] _collarBones = Array.Empty<Transform3D>();
    private Transform3D _collarInverse;
    private Aabb _collarBoundsAll;

    private void FitCollar(List<AttachmentVertex> vertices, List<int> indices)
    {
        var selected = new List<AttachmentVertex>();
        var triangles = new List<int>();
        var remap = new Dictionary<int, int>();
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = vertices[indices[i]].Point;
            var b = vertices[indices[i + 1]].Point;
            var c = vertices[indices[i + 2]].Point;
            var min = a.Min(b).Min(c); var max = a.Max(b).Max(c);
            if (min.Y > 0.1f * _rigScale || max.Y < -0.3f * _rigScale
                || min.Z > 0.02f * _rigScale || max.Z < -0.4f * _rigScale) continue;
            for (int j = 0; j < 3; j++)
            {
                int original = indices[i + j];
                if (!remap.TryGetValue(original, out int index))
                {
                    index = selected.Count;
                    selected.Add(vertices[original]); remap.Add(original, index);
                }
                triangles.Add(index);
            }
        }
        _collarVertices = selected.ToArray();
        _collarIndices = triangles.ToArray();
        _collarPos = new Vector3[selected.Count];
        _collarBounds = new Aabb[triangles.Count / 3];
        _collarBones = new Transform3D[_skel.GetBoneCount()];
        _collarCells = new int[CollarGridSize * CollarGridSize * _collarBounds.Length];
        _collarU = new Vector2[_collarBounds.Length];
        _collarV = new Vector2[_collarBounds.Length];
        _collarPlanes = new Vector3[_collarBounds.Length];
    }

    private void UpdateCollar()
    {
        _collarInverse = _pinCurrent.AffineInverse();
        var toCollar = _collarInverse * _skel.GlobalTransform;
        for (int i = 0; i < _collarBones.Length; i++)
            _collarBones[i] = toCollar * _skel.GetBoneGlobalPose(i);
        for (int i = 0; i < _collarVertices.Length; i++)
        {
            var vertex = _collarVertices[i];
            var p = Vector3.Zero;
            foreach (var influence in vertex.Influences)
                p += (_collarBones[influence.Bone] * influence.Point) * influence.Weight;
            _collarPos[i] = vertex.Influences.Length > 0 ? p : vertex.Point;
        }
        for (int i = 0; i < _collarBounds.Length; i++)
        {
            int at = i * 3;
            var a = _collarPos[_collarIndices[at]];
            var b = _collarPos[_collarIndices[at + 1]];
            var c = _collarPos[_collarIndices[at + 2]];
            var ab = b - a; var ac = c - a;
            float determinant = ab.X * ac.Y - ab.Y * ac.X;
            if (Mathf.Abs(determinant) > 1e-9f)
            {
                _collarU[i] = new Vector2(ac.Y, -ac.X) / determinant;
                _collarV[i] = new Vector2(-ab.Y, ab.X) / determinant;
                var slope = _collarU[i] * ab.Z + _collarV[i] * ac.Z;
                _collarPlanes[i] = new Vector3(slope.X, slope.Y,
                    a.Z - slope.X * a.X - slope.Y * a.Y - 0.015f * _rigScale);
            }
            else _collarU[i] = _collarV[i] = Vector2.Zero;
            var min = a.Min(b).Min(c); var max = a.Max(b).Max(c);
            var bounds = new Aabb(min, max - min).Grow(0.02f * _rigScale);
            bounds.Size += new Vector3(0, 0, 0.2f * _rigScale);
            _collarBounds[i] = bounds;
            _collarBoundsAll = i == 0 ? bounds : _collarBoundsAll.Merge(bounds);
        }
        Array.Clear(_collarCellCounts);
        var size = _collarBoundsAll.Size;
        _collarGridScale = new Vector2(CollarGridSize / Mathf.Max(size.X, 1e-5f), CollarGridSize / Mathf.Max(size.Y, 1e-5f));
        for (int i = 0; i < _collarBounds.Length; i++)
        {
            var from = CollarCell(_collarBounds[i].Position);
            var to = CollarCell(_collarBounds[i].End);
            for (int y = from.Y; y <= to.Y; y++)
                for (int x = from.X; x <= to.X; x++)
                {
                    int cell = y * CollarGridSize + x;
                    _collarCells[cell * _collarBounds.Length + _collarCellCounts[cell]++] = i;
                }
        }
    }

    private Vector2I CollarCell(Vector3 p)
    {
        var d = p - _collarBoundsAll.Position;
        return new Vector2I(Mathf.Clamp((int)(d.X * _collarGridScale.X), 0, CollarGridSize - 1),
            Mathf.Clamp((int)(d.Y * _collarGridScale.Y), 0, CollarGridSize - 1));
    }

    private void CollideCollar(ref Vector3 point)
    {
        var p = _collarInverse * point;
        if (!_collarBoundsAll.HasPoint(p)) return;
        bool moved = false;
        var cell = CollarCell(p);
        int bucket = cell.Y * CollarGridSize + cell.X;
        int start = bucket * _collarBounds.Length;
        for (int candidate = 0; candidate < _collarCellCounts[bucket]; candidate++)
        {
            int i = _collarCells[start + candidate];
            if (!_collarBounds[i].HasPoint(p) || _collarU[i] == Vector2.Zero) continue;
            var a = _collarPos[_collarIndices[i * 3]];
            var d = new Vector2(p.X - a.X, p.Y - a.Y);
            float u = d.Dot(_collarU[i]), v = d.Dot(_collarV[i]);
            if (u < -0.05f || v < -0.05f || u + v > 1.05f) continue;
            var plane = _collarPlanes[i];
            float back = plane.X * p.X + plane.Y * p.Y + plane.Z;
            if (p.Z <= back) continue;
            p.Z = back;
            moved = true;
        }
        if (moved) point = _pinCurrent * p;
    }
}
