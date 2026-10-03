using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class WindowPlacement
{
    public const float CentreMargin = 8f;
    public const float PlateTop = 12f;
    public const float PlateGap = 6f;
    public const float PlateHudBand = 40f;

    public static Vector2 Centre(Vector2 screen, Vector2 size, float margin = CentreMargin) =>
        new(CentreOn(screen.X, size.X, margin), CentreOn(screen.Y, size.Y, margin));

    private static float CentreOn(float screen, float size, float margin) =>
        Mathf.Clamp(Mathf.Floor((screen - size) * 0.5f), margin, Mathf.Max(margin, screen - size - margin));

    public static Vector2[] TopCentreStack(Vector2 screen, IReadOnlyList<Vector2> sizes, IReadOnlyList<Rect2> hud,
                                           float bottomInset)
    {
        var spots = new Vector2[sizes.Count];
        float next = PlateTop;
        for (int i = 0; i < sizes.Count; i++)
        {
            Vector2 size = sizes[i];
            float x = Mathf.Max(0f, Mathf.Floor((screen.X - size.X) * 0.5f));
            float top = next;
            foreach (var rect in hud)
                if (rect.Position.Y <= PlateHudBand && rect.Position.X < x + size.X && rect.End.X > x)
                    top = Mathf.Max(top, rect.End.Y + PlateGap);
            top = Mathf.Max(0f, Mathf.Min(top, screen.Y - bottomInset - size.Y));
            spots[i] = new Vector2(x, Mathf.Floor(top));
            next = spots[i].Y + size.Y + PlateGap;
        }
        return spots;
    }
}
