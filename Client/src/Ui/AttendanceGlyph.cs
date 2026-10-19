using Godot;

namespace LibreKO;

public partial class AttendanceGlyph : Control
{
    public enum Kind { Check, Lock }

    private Kind kind;
    private Color color;
    private float size;

    public static AttendanceGlyph Make(Kind kind, Color color, float size = 22f)
    {
        var glyph = new AttendanceGlyph
        {
            kind = kind,
            color = color,
            size = size,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        glyph.SetAnchorsPreset(LayoutPreset.FullRect);
        glyph.Resized += glyph.QueueRedraw;
        return glyph;
    }

    public override void _Draw()
    {
        Vector2 c = Size * 0.5f;
        float s = size;
        DrawCircle(c, s * 0.62f, new Color(0f, 0f, 0f, 0.55f));
        if (kind == Kind.Check)
        {
            var points = new[]
            {
                c + new Vector2(-s * 0.30f, s * 0.02f),
                c + new Vector2(-s * 0.08f, s * 0.26f),
                c + new Vector2(s * 0.34f, -s * 0.26f),
            };
            DrawPolyline(points, color, Mathf.Max(2f, s * 0.14f), true);
            return;
        }

        float w = s * 0.46f, h = s * 0.36f;
        var body = new Rect2(c.X - w * 0.5f, c.Y - h * 0.15f, w, h);
        DrawRect(body, color);
        DrawArc(new Vector2(c.X, body.Position.Y), w * 0.32f, Mathf.Pi, Mathf.Tau, 12, color,
            Mathf.Max(2f, s * 0.10f), true);
    }
}
