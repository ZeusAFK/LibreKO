using Godot;

namespace LibreKO.Domain;

public static class PowerUpStoreLayout
{
    public static readonly Vector2 PointerMinimum = new(900, 560);
    public static readonly Vector2 PointerMaximum = new(1360, 900);
    public const float PointerScreenShare = 0.8f;
    public const float ScreenMargin = 16f;

    public static Vector2 WindowSize(Vector2 screen, bool touch)
    {
        if (touch) return screen;
        var size = (screen * PointerScreenShare).Clamp(PointerMinimum, PointerMaximum);
        var room = screen - new Vector2(ScreenMargin, ScreenMargin) * 2;
        return new Vector2(Mathf.Min(size.X, room.X), Mathf.Min(size.Y, room.Y)).Floor();
    }

    public static int Columns(float width, float cardWidth, float gap) =>
        Mathf.Max(1, Mathf.FloorToInt((width + gap) / (cardWidth + gap)));
}
