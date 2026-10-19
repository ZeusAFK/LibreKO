using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO;

public partial class Cape : MeshInstance3D
{
    private const int CollarGridSize = 8;
    private readonly int[] collarCellCounts = new int[CollarGridSize * CollarGridSize];
    private int[] collarCells = Array.Empty<int>();
    private Vector2[] collarU = Array.Empty<Vector2>(), collarV = Array.Empty<Vector2>();
    private Vector3[] collarPlanes = Array.Empty<Vector3>();
    private Vector2 collarGridScale;
    private AttachmentVertex[] collarVertices = Array.Empty<AttachmentVertex>();
    private Vector3[] collarPos = Array.Empty<Vector3>();
    private int[] collarIndices = Array.Empty<int>();
    private Aabb[] collarBounds = Array.Empty<Aabb>();
    private Transform3D[] collarBones = Array.Empty<Transform3D>();
    private Transform3D collarInverse;
    private Aabb collarBoundsAll;

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
            if (min.Y > 0.1f * rigScale || max.Y < -0.3f * rigScale
                || min.Z > 0.02f * rigScale || max.Z < -0.4f * rigScale) continue;
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
        collarVertices = selected.ToArray();
        collarIndices = triangles.ToArray();
        collarPos = new Vector3[selected.Count];
        collarBounds = new Aabb[triangles.Count / 3];
        collarBones = new Transform3D[skel.GetBoneCount()];
        collarCells = new int[CollarGridSize * CollarGridSize * collarBounds.Length];
        collarU = new Vector2[collarBounds.Length];
        collarV = new Vector2[collarBounds.Length];
        collarPlanes = new Vector3[collarBounds.Length];
    }

    private void UpdateCollar()
    {
        collarInverse = pinCurrent.AffineInverse();
        var toCollar = collarInverse * skel.GlobalTransform;
        for (int i = 0; i < collarBones.Length; i++)
            collarBones[i] = toCollar * skel.GetBoneGlobalPose(i);
        for (int i = 0; i < collarVertices.Length; i++)
        {
            var vertex = collarVertices[i];
            var p = Vector3.Zero;
            foreach (var influence in vertex.Influences)
                p += (collarBones[influence.Bone] * influence.Point) * influence.Weight;
            collarPos[i] = vertex.Influences.Length > 0 ? p : vertex.Point;
        }
        for (int i = 0; i < collarBounds.Length; i++)
        {
            int at = i * 3;
            var a = collarPos[collarIndices[at]];
            var b = collarPos[collarIndices[at + 1]];
            var c = collarPos[collarIndices[at + 2]];
            var ab = b - a; var ac = c - a;
            float determinant = ab.X * ac.Y - ab.Y * ac.X;
            if (Mathf.Abs(determinant) > 1e-9f)
            {
                collarU[i] = new Vector2(ac.Y, -ac.X) / determinant;
                collarV[i] = new Vector2(-ab.Y, ab.X) / determinant;
                var slope = collarU[i] * ab.Z + collarV[i] * ac.Z;
                collarPlanes[i] = new Vector3(slope.X, slope.Y,
                    a.Z - slope.X * a.X - slope.Y * a.Y - 0.015f * rigScale);
            }
            else collarU[i] = collarV[i] = Vector2.Zero;
            var min = a.Min(b).Min(c); var max = a.Max(b).Max(c);
            var bounds = new Aabb(min, max - min).Grow(0.02f * rigScale);
            bounds.Size += new Vector3(0, 0, 0.2f * rigScale);
            collarBounds[i] = bounds;
            collarBoundsAll = i == 0 ? bounds : collarBoundsAll.Merge(bounds);
        }
        Array.Clear(collarCellCounts);
        var size = collarBoundsAll.Size;
        collarGridScale = new Vector2(CollarGridSize / Mathf.Max(size.X, 1e-5f), CollarGridSize / Mathf.Max(size.Y, 1e-5f));
        for (int i = 0; i < collarBounds.Length; i++)
        {
            var from = CollarCell(collarBounds[i].Position);
            var to = CollarCell(collarBounds[i].End);
            for (int y = from.Y; y <= to.Y; y++)
                for (int x = from.X; x <= to.X; x++)
                {
                    int cell = y * CollarGridSize + x;
                    collarCells[cell * collarBounds.Length + collarCellCounts[cell]++] = i;
                }
        }
    }

    private Vector2I CollarCell(Vector3 p)
    {
        var d = p - collarBoundsAll.Position;
        return new Vector2I(Mathf.Clamp((int)(d.X * collarGridScale.X), 0, CollarGridSize - 1),
            Mathf.Clamp((int)(d.Y * collarGridScale.Y), 0, CollarGridSize - 1));
    }

    private void CollideCollar(ref Vector3 point)
    {
        var p = collarInverse * point;
        if (!collarBoundsAll.HasPoint(p)) return;
        bool moved = false;
        var cell = CollarCell(p);
        int bucket = cell.Y * CollarGridSize + cell.X;
        int start = bucket * collarBounds.Length;
        for (int candidate = 0; candidate < collarCellCounts[bucket]; candidate++)
        {
            int i = collarCells[start + candidate];
            if (!collarBounds[i].HasPoint(p) || collarU[i] == Vector2.Zero) continue;
            var a = collarPos[collarIndices[i * 3]];
            var d = new Vector2(p.X - a.X, p.Y - a.Y);
            float u = d.Dot(collarU[i]), v = d.Dot(collarV[i]);
            if (u < -0.05f || v < -0.05f || u + v > 1.05f) continue;
            var plane = collarPlanes[i];
            float back = plane.X * p.X + plane.Y * p.Y + plane.Z;
            if (p.Z <= back) continue;
            p.Z = back;
            moved = true;
        }
        if (moved) point = pinCurrent * p;
    }
}
