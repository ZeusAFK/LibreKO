using Godot;

namespace LibreKO;

public partial class MapChevron : Control
{
    private static readonly Color Stroke = new(0.93f, 0.94f, 0.96f, 0.90f);
    private static readonly Color Shade = new(0f, 0f, 0f, 0.55f);

    private bool _pointsDown;

    public bool PointsDown
    {
        set
        {
            if (_pointsDown == value) return;
            _pointsDown = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        float w = Size.X * 0.30f;
        float h = Size.Y * 0.14f;
        var c = Size * 0.5f;
        float dir = _pointsDown ? -1f : 1f;
        var left = c + new Vector2(-w, h * dir);
        var apex = c + new Vector2(0f, -h * dir);
        var right = c + new Vector2(w, h * dir);
        float thickness = Mathf.Max(1.5f, Size.Y * 0.075f);
        var shadow = new Vector2(0f, 1.5f);
        DrawPolyline(new[] { left + shadow, apex + shadow, right + shadow },
                     Shade, thickness, true);
        DrawPolyline(new[] { left, apex, right }, Stroke, thickness, true);
    }
}
