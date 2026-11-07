using Godot;

namespace LibreKO;

internal static class VertexClusters
{
    internal static (int[] Vertices, int[] Triangles) Reduce(IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles,
        int triangleLimit, float firstCell)
    {
        for (float cell = firstCell; ; cell *= 2f)
        {
            var reduced = Cluster(points, triangles, cell);
            if (reduced.Triangles.Length / 3 <= triangleLimit) return reduced;
        }
    }

    private static (int[] Vertices, int[] Triangles) Cluster(IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles, float cell)
    {
        var firstInCell = new Dictionary<Vector3I, int>();
        var representative = new int[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            var scaled = points[i] / cell;
            var key = new Vector3I(Mathf.FloorToInt(scaled.X), Mathf.FloorToInt(scaled.Y), Mathf.FloorToInt(scaled.Z));
            if (!firstInCell.TryGetValue(key, out int first)) firstInCell.Add(key, first = i);
            representative[i] = first;
        }

        var compact = new Dictionary<int, int>();
        var vertices = new List<int>();
        var kept = new List<int>();
        var seen = new HashSet<(int, int, int)>();
        for (int t = 0; t + 2 < triangles.Count; t += 3)
        {
            int a = representative[triangles[t]], b = representative[triangles[t + 1]], c = representative[triangles[t + 2]];
            if (a == b || b == c || a == c || !seen.Add(Ordered(a, b, c))) continue;
            kept.Add(Compact(a));
            kept.Add(Compact(b));
            kept.Add(Compact(c));
        }
        return (vertices.ToArray(), kept.ToArray());

        int Compact(int original)
        {
            if (!compact.TryGetValue(original, out int index))
            {
                index = vertices.Count;
                vertices.Add(original);
                compact.Add(original, index);
            }
            return index;
        }
    }

    private static (int, int, int) Ordered(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }
}
