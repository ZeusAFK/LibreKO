using Godot;
using Xunit;

namespace LibreKO.Tests;

public class VertexClustersTests
{
    private const float Size = 0.4f;
    private const int DenseSide = 60;
    private const int SparseSide = 4;
    private const int Limit = 400;
    private const float Cell = 0.01f;

    private static (List<Vector3> Points, List<int> Triangles) Grid(int side)
    {
        var points = new List<Vector3>();
        var triangles = new List<int>();
        for (int y = 0; y <= side; y++)
            for (int x = 0; x <= side; x++)
                points.Add(new Vector3(x * Size / side, y * Size / side, 0.05f * Mathf.Sin(x * 0.3f)));
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int at = y * (side + 1) + x;
                triangles.AddRange(new[] { at, at + 1, at + side + 1, at + 1, at + side + 2, at + side + 1 });
            }
        return (points, triangles);
    }

    [Fact]
    public void ADenseMeshComesDownToTheTriangleLimit()
    {
        var (points, triangles) = Grid(DenseSide);
        var (vertices, reduced) = VertexClusters.Reduce(points, triangles, Limit, Cell);
        Assert.InRange(reduced.Length / 3, 1, Limit);
        Assert.All(reduced, index => Assert.InRange(index, 0, vertices.Length - 1));
        for (int t = 0; t < reduced.Length; t += 3)
            Assert.True(reduced[t] != reduced[t + 1] && reduced[t + 1] != reduced[t + 2] && reduced[t] != reduced[t + 2]);
    }

    [Fact]
    public void KeptVerticesAreOriginalPointsThatStillSpanTheMesh()
    {
        var (points, triangles) = Grid(DenseSide);
        var (vertices, _) = VertexClusters.Reduce(points, triangles, Limit, Cell);
        Assert.Equal(vertices.Length, vertices.Distinct().Count());
        Assert.All(vertices, original => Assert.InRange(original, 0, points.Count - 1));
        float spanX = vertices.Max(i => points[i].X) - vertices.Min(i => points[i].X);
        Assert.True(spanX > Size * 0.8f);
    }

    [Fact]
    public void AMeshCoarserThanTheCellKeepsEveryTriangle()
    {
        var (points, triangles) = Grid(SparseSide);
        var (vertices, reduced) = VertexClusters.Reduce(points, triangles, Limit, Cell);
        Assert.Equal(triangles.Count, reduced.Length);
        Assert.Equal(points.Count, vertices.Length);
    }
}
