using Godot;

namespace LibreKO;

public partial class ChatBubbleGlyph : Control
{
    private static readonly Color Stroke = new(0.80f, 0.83f, 0.87f, 0.85f);
    private static readonly Color Shade = new(0f, 0f, 0f, 0.50f);

    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y * 0.74f;
        float r = h * 0.28f;
        float line = Mathf.Max(1.5f, Size.Y * 0.075f);
        var body = new Rect2(line, line, w - line * 2f, h - line);

        DrawRect(new Rect2(body.Position + Vector2.One, body.Size), Shade, false, line);
        DrawRect(body, Stroke, false, line);

        var tailBase = new Vector2(body.Position.X + body.Size.X * 0.28f, body.End.Y);
        DrawPolyline(new[]
        {
            tailBase,
            new Vector2(tailBase.X + r * 0.30f, Size.Y - line),
            new Vector2(tailBase.X + r * 1.35f, body.End.Y),
        }, Stroke, line, true);

        float dotY = body.Position.Y + body.Size.Y * 0.5f;
        float dotR = Mathf.Max(1f, Size.Y * 0.055f);
        for (int i = 0; i < 3; i++)
            DrawCircle(new Vector2(body.Position.X + body.Size.X * (0.28f + i * 0.22f), dotY),
                       dotR, Stroke, true, -1f, true);
    }
}
