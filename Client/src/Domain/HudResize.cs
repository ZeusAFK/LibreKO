using Godot;

namespace LibreKO.Domain;

public static class HudResize
{
    public static Vector2 Limit(Vector2 viewport, Vector2 position, Rect2 origin, bool fromLeft, bool fromTop) =>
        new(fromLeft ? origin.End.X : viewport.X - Mathf.Max(0f, position.X),
            fromTop ? origin.End.Y : viewport.Y - Mathf.Max(0f, position.Y));
}
