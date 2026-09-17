using Godot;

namespace LibreKO;

public partial class BadgeDisc : Control
{
    private static readonly Color Rim = new(0f, 0f, 0f, 0.75f);

    public Color Fill = UiTheme.Bad;

    public override void _Draw()
    {
        var centre = Size * 0.5f;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.5f;
        if (radius <= 0f) return;
        DrawCircle(centre, radius, Rim, true, -1f, true);
        DrawCircle(centre, Mathf.Max(0f, radius - 1.5f), Fill, true, -1f, true);
    }
}
