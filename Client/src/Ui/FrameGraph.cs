using Godot;

namespace LibreKO;

public partial class FrameGraph : Control
{
    private const int Frames = 300;
    private const float MinScaleMs = 120f;
    private static readonly float[] GridMs = { 16.7f, 33.3f, 66.7f, 100f, 200f, 400f };
    private static readonly Color Fill = new(0.22f, 0.62f, 0.72f, 0.35f);
    private static readonly Color Line = new(0.35f, 0.85f, 0.95f, 0.95f);
    private static readonly Color Grid = new(1f, 1f, 1f, 0.10f);
    private static readonly Color Back = new(0.06f, 0.07f, 0.08f, 0.72f);
    private static readonly Color Text = new(0.72f, 0.77f, 0.84f, 0.9f);
    private const int LabelSize = 11;

    private readonly float[] _ms = new float[Frames];
    private readonly Vector2[] _polygon = new Vector2[Frames + 2];
    private readonly Vector2[] _outline = new Vector2[Frames];
    private int _count;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _count = Perf.CopyRecentFrames(_ms);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), Back);
        if (_count < 2) return;

        float peak = MinScaleMs;
        for (int i = 0; i < _count; i++) if (_ms[i] > peak) peak = _ms[i];
        float scale = (size.Y - 2f) / peak;
        var font = ThemeDB.FallbackFont;
        foreach (float grid in GridMs)
        {
            if (grid > peak) break;
            float y = size.Y - grid * scale;
            DrawLine(new Vector2(0f, y), new Vector2(size.X, y), Grid, 1f);
            DrawString(font, new Vector2(4f, y - 2f), $"{grid:0} ms", HorizontalAlignment.Left, -1, LabelSize, Text);
        }

        float step = size.X / (Frames - 1);
        float x0 = size.X - (_count - 1) * step;
        for (int i = 0; i < _count; i++)
        {
            float x = x0 + i * step;
            float y = size.Y - Mathf.Min(_ms[i], peak) * scale;
            _outline[i] = new Vector2(x, y);
            _polygon[i + 1] = _outline[i];
        }
        _polygon[0] = new Vector2(x0, size.Y);
        _polygon[_count + 1] = new Vector2(x0 + (_count - 1) * step, size.Y);
        DrawColoredPolygon(_polygon[..(_count + 2)], Fill);
        DrawPolyline(_outline[.._count], Line, 1.5f, true);
    }
}
