using Godot;

namespace LibreKO.Features.Hud;

internal enum HotToolGlyph { Locked, Unlocked, Turn, Add, Remove }

internal sealed partial class HotGrip : Control
{
    public bool Vertical;
    private static readonly Color Dot = new(UiTheme.TextLo, 0.75f);
    private const float DotStep = 6f;
    private const float DotRadius = 1.2f;

    public override void _Draw()
    {
        var style = new StyleBoxFlat { BgColor = new Color(UiTheme.Glass, 0.6f) };
        style.SetCornerRadiusAll(3);
        DrawStyleBox(style, new Rect2(Vector2.Zero, Size));
        float along = Vertical ? Size.X : Size.Y;
        float across = (Vertical ? Size.Y : Size.X) * 0.5f;
        for (float t = DotStep; t < along - DotStep * 0.5f; t += DotStep)
            DrawCircle(Vertical ? new Vector2(t, across) : new Vector2(across, t), DotRadius, Dot);
    }
}

internal sealed partial class HotToolButton : Button
{
    public HotToolGlyph Glyph;

    public override void _Draw()
    {
        var tint = Disabled ? new Color(UiTheme.TextLo, 0.4f)
            : Glyph == HotToolGlyph.Locked ? UiTheme.Bad
            : Glyph == HotToolGlyph.Unlocked ? UiTheme.Good
            : UiTheme.GoldBright;
        float w = Size.X, h = Size.Y, cx = w * 0.5f, cy = h * 0.5f;
        switch (Glyph)
        {
            case HotToolGlyph.Locked:
            case HotToolGlyph.Unlocked:
            {
                var body = new Rect2(w * 0.28f, h * 0.48f, w * 0.44f, h * 0.30f);
                DrawRect(body, tint, Glyph == HotToolGlyph.Locked);
                if (Glyph == HotToolGlyph.Unlocked) DrawRect(body, tint, false, 1f);
                float r = w * 0.15f, arcCy = h * 0.40f;
                float lift = Glyph == HotToolGlyph.Unlocked ? h * 0.08f : 0f;
                DrawArc(new Vector2(cx, arcCy - lift), r, Mathf.Pi, Mathf.Tau, 12, tint, 1.5f);
                DrawLine(new Vector2(cx - r, arcCy - lift), new Vector2(cx - r, body.Position.Y - lift), tint, 1.5f);
                DrawLine(new Vector2(cx + r, arcCy), new Vector2(cx + r, body.Position.Y), tint, 1.5f);
                break;
            }
            case HotToolGlyph.Turn:
            {
                float r = w * 0.26f;
                DrawArc(new Vector2(cx, cy), r, -Mathf.Pi * 0.9f, Mathf.Pi * 0.4f, 16, tint, 1.5f);
                var tip = new Vector2(cx + r * Mathf.Cos(Mathf.Pi * 0.4f), cy + r * Mathf.Sin(Mathf.Pi * 0.4f));
                DrawLine(tip, tip + new Vector2(w * 0.14f, 0f), tint, 1.5f);
                DrawLine(tip, tip + new Vector2(0f, -h * 0.14f), tint, 1.5f);
                break;
            }
            case HotToolGlyph.Add:
                DrawLine(new Vector2(w * 0.28f, cy), new Vector2(w * 0.72f, cy), tint, 2f);
                DrawLine(new Vector2(cx, h * 0.28f), new Vector2(cx, h * 0.72f), tint, 2f);
                break;
            case HotToolGlyph.Remove:
                DrawLine(new Vector2(w * 0.28f, cy), new Vector2(w * 0.72f, cy), tint, 2f);
                break;
        }
    }
}
