using Godot;

namespace LibreKO;

public sealed partial class CheckGlyph : Control
{
    private const float StrokeWidth = 2.5f;
    private static readonly Vector2[] Stroke = [new(0.12f, 0.55f), new(0.4f, 0.82f), new(0.88f, 0.2f)];

    private readonly Color colour;

    public CheckGlyph(float side, Color colour)
    {
        this.colour = colour;
        CustomMinimumSize = new Vector2(side, side);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var points = new Vector2[Stroke.Length];
        for (int i = 0; i < Stroke.Length; i++) points[i] = Stroke[i] * Size;
        DrawPolyline(points, colour, StrokeWidth, antialiased: true);
    }
}
