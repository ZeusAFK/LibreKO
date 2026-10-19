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

    private readonly float[] frameMs = new float[Frames];
    private readonly Vector2[] polygon = new Vector2[Frames + 2];
    private readonly Vector2[] outline = new Vector2[Frames];
    private int count;

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        count = Perf.CopyRecentFrames(frameMs);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), Back);
        if (count < 2) return;

        float peak = MinScaleMs;
        for (int i = 0; i < count; i++) if (frameMs[i] > peak) peak = frameMs[i];
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
        float x0 = size.X - (count - 1) * step;
        for (int i = 0; i < count; i++)
        {
            float x = x0 + i * step;
            float y = size.Y - Mathf.Min(frameMs[i], peak) * scale;
            outline[i] = new Vector2(x, y);
            polygon[i + 1] = outline[i];
        }
        polygon[0] = new Vector2(x0, size.Y);
        polygon[count + 1] = new Vector2(x0 + (count - 1) * step, size.Y);
        DrawColoredPolygon(polygon[..(count + 2)], Fill);
        DrawPolyline(outline[..count], Line, 1.5f, true);
    }
}
