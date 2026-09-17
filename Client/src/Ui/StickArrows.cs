using Godot;

namespace LibreKO;

public partial class StickArrows : Control
{
    private static readonly Color Fill = new(0.85f, 0.88f, 0.93f, 0.34f);

    public float Reach = 0.70f;
    public float Wing = 0.085f;

    public override void _Draw()
    {
        var centre = Size * 0.5f;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.5f;
        if (radius <= 0f) return;
        float reach = radius * Reach;
        float wing = radius * Wing;

        for (int i = 0; i < 4; i++)
        {
            float angle = Mathf.Pi * 0.5f * i;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var side = new Vector2(-dir.Y, dir.X);
            var tip = centre + dir * reach;
            DrawColoredPolygon(new[]
            {
                tip,
                tip - dir * wing * 1.7f + side * wing,
                tip - dir * wing * 1.7f - side * wing,
            }, Fill);
        }
    }
}
