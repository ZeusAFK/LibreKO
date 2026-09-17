using Godot;

namespace LibreKO;

public static class HudAnchor
{
    public enum Spot
    {
        TopLeft,
        TopCenter,
        TopRight,
        MidLeft,
        MidRight,
        BottomLeft,
        BottomCenter,
        BottomRight,
    }

    public const float Edge = 12f;
    public const float ClockHeight = 26f;

    public static void Pin(Control target, Spot spot, Vector2 margin = default, Vector2 size = default)
    {
        switch (spot)
        {
            case Spot.TopLeft:
            case Spot.MidLeft:
            case Spot.BottomLeft:
                target.AnchorLeft = target.AnchorRight = 0f;
                target.GrowHorizontal = Control.GrowDirection.End;
                target.OffsetLeft = margin.X;
                target.OffsetRight = margin.X + size.X;
                break;
            case Spot.TopCenter:
            case Spot.BottomCenter:
                target.AnchorLeft = target.AnchorRight = 0.5f;
                target.GrowHorizontal = Control.GrowDirection.Both;
                target.OffsetLeft = margin.X - size.X * 0.5f;
                target.OffsetRight = margin.X + size.X * 0.5f;
                break;
            default:
                target.AnchorLeft = target.AnchorRight = 1f;
                target.GrowHorizontal = Control.GrowDirection.Begin;
                target.OffsetLeft = -margin.X - size.X;
                target.OffsetRight = -margin.X;
                break;
        }

        switch (spot)
        {
            case Spot.TopLeft:
            case Spot.TopCenter:
            case Spot.TopRight:
                target.AnchorTop = target.AnchorBottom = 0f;
                target.GrowVertical = Control.GrowDirection.End;
                target.OffsetTop = margin.Y;
                target.OffsetBottom = margin.Y + size.Y;
                break;
            case Spot.MidLeft:
            case Spot.MidRight:
                target.AnchorTop = target.AnchorBottom = 0.5f;
                target.GrowVertical = Control.GrowDirection.Both;
                target.OffsetTop = margin.Y - size.Y * 0.5f;
                target.OffsetBottom = margin.Y + size.Y * 0.5f;
                break;
            default:
                target.AnchorTop = target.AnchorBottom = 1f;
                target.GrowVertical = Control.GrowDirection.Begin;
                target.OffsetTop = -margin.Y - size.Y;
                target.OffsetBottom = -margin.Y;
                break;
        }
    }
}
