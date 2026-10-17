using System;
using Godot;

namespace LibreKO;

public sealed class DiscBatch
{
    public const int Segments = 64;
    private const int IndicesPerTriangle = 3;
    public const int VerticesPerDisc = Segments + 2;
    public const int IndicesPerDisc = Segments * IndicesPerTriangle;
    private const int CentreVertex = Segments + 1;
    private const int InitialDiscs = 32;
    private const int AllIndices = -1;

    private static readonly float[] RimCos = Rim(MathF.Cos);
    private static readonly float[] RimSin = Rim(MathF.Sin);

    private Vector2[] _points = new Vector2[VerticesPerDisc * InitialDiscs];
    private Color[] _colors = new Color[VerticesPerDisc * InitialDiscs];
    private int[] _indices = new int[IndicesPerDisc * InitialDiscs];
    private int _indexed;

    public int Count { get; private set; }

    public ReadOnlySpan<Vector2> Points => _points.AsSpan(0, Count * VerticesPerDisc);
    public ReadOnlySpan<Color> Colors => _colors.AsSpan(0, Count * VerticesPerDisc);
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, Count * IndicesPerDisc);

    private static float[] Rim(Func<float, float> wave)
    {
        float step = (float)(Math.Tau / Segments);
        var rim = new float[Segments + 1];
        for (int i = 0; i <= Segments; i++) rim[i] = wave(i * step);
        return rim;
    }

    public void Add(Vector2 centre, float radius, Color color)
    {
        if ((Count + 1) * VerticesPerDisc > _points.Length) Grow();
        int first = Count * VerticesPerDisc;
        for (int i = 0; i <= Segments; i++)
            _points[first + i] = new Vector2(RimCos[i] * radius + centre.X, RimSin[i] * radius + centre.Y);
        _points[first + CentreVertex] = centre;
        _colors.AsSpan(first, VerticesPerDisc).Fill(color);
        Count++;
        for (; _indexed < Count; _indexed++) Index(_indexed);
    }

    public void Clear() => Count = 0;

    public void Flush(Rid canvasItem)
    {
        if (Count == 0) return;
        RenderingServer.CanvasItemAddTriangleArray(canvasItem, Indices, Points, Colors,
            ReadOnlySpan<Vector2>.Empty, ReadOnlySpan<int>.Empty, ReadOnlySpan<float>.Empty, default, AllIndices);
        Clear();
    }

    private void Index(int disc)
    {
        int first = disc * VerticesPerDisc;
        int at = disc * IndicesPerDisc;
        for (int i = 0; i < Segments; i++, at += IndicesPerTriangle)
        {
            _indices[at] = first + CentreVertex;
            _indices[at + 1] = first + i;
            _indices[at + 2] = first + i + 1;
        }
    }

    private void Grow()
    {
        Array.Resize(ref _points, _points.Length * 2);
        Array.Resize(ref _colors, _colors.Length * 2);
        Array.Resize(ref _indices, _indices.Length * 2);
    }
}
