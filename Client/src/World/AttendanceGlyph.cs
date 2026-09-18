using Godot;

namespace LibreKO;

public partial class AttendanceGlyph : Control
{
    public enum Kind { Check, Lock }

    private Kind _kind;
    private Color _color;
    private float _size;

    public static AttendanceGlyph Make(Kind kind, Color color, float size = 22f)
    {
        var glyph = new AttendanceGlyph
        {
            _kind = kind,
            _color = color,
            _size = size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        glyph.SetAnchorsPreset(LayoutPreset.FullRect);
        glyph.Resized += glyph.QueueRedraw;
        return glyph;
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float s = _size;
        DrawCircle(c, s * 0.62f, new Color(0f, 0f, 0f, 0.55f));
        if (_kind == Kind.Check)
        {
            var points = new[]
            {
                c + new Vector2(-s * 0.30f, s * 0.02f),
                c + new Vector2(-s * 0.08f, s * 0.26f),
                c + new Vector2(s * 0.34f, -s * 0.26f),
            };
            DrawPolyline(points, _color, Mathf.Max(2f, s * 0.14f), true);
            return;
        }

        float w = s * 0.46f, h = s * 0.36f;
        var body = new Rect2(c.X - w * 0.5f, c.Y - h * 0.15f, w, h);
        DrawRect(body, _color);
        DrawArc(new Vector2(c.X, body.Position.Y), w * 0.32f, Mathf.Pi, Mathf.Tau, 12, _color,
            Mathf.Max(2f, s * 0.10f), true);
    }
}
