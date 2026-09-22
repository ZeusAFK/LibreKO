using Godot;

namespace LibreKO;

public static class HudPlacement
{
    public const float TouchEdge = 18f;
    public const float ExpBarHeight = 12f;
    public const float StripTextHeight = 26f;

    public const float BuffChipSize = 46f;

    public static float BottomInset => ExpBarHeight + StripTextHeight;

    private const float BuffRowGap = 10f;

    public readonly record struct Slot(HudAnchor.Spot Anchor, Vector2 Margin, Vector2 Size)
    {
        public void ApplyTo(Control target) => HudAnchor.Pin(target, Anchor, Margin, Size);
    }

    public static Slot MiniMap => Platform.TouchUi
        ? new(HudAnchor.Spot.TopLeft, new Vector2(TouchEdge, TouchEdge), Vector2.Zero)
        : new(HudAnchor.Spot.TopRight,
              new Vector2(HudAnchor.Edge, HudAnchor.Edge + HudAnchor.ClockHeight), Vector2.Zero);

    public static Slot Launcher(Vector2 pointerMargin) => Platform.TouchUi
        ? new(HudAnchor.Spot.TopRight, new Vector2(TouchEdge, TouchEdge), Vector2.Zero)
        : new(HudAnchor.Spot.TopLeft, pointerMargin, Vector2.Zero);

    public static Slot AttendanceGift => new(
        HudAnchor.Spot.TopLeft,
        new Vector2(SideIconLeft, TouchEdge + LauncherButtonSize + TouchEdge),
        Vector2.Zero);

    public static Slot AchievementTrophy => new(
        HudAnchor.Spot.TopLeft,
        new Vector2(SideIconLeft, TouchEdge + (LauncherButtonSize + TouchEdge) * 2f),
        Vector2.Zero);

    public static Slot MailIcon => new(
        HudAnchor.Spot.TopLeft,
        new Vector2(SideIconLeft, TouchEdge + (LauncherButtonSize + TouchEdge) * 3f),
        Vector2.Zero);

    public static Slot TownButton => new(
        HudAnchor.Spot.TopLeft,
        new Vector2(SideIconLeft, TouchEdge),
        Vector2.Zero);

    public static Slot QuestTracker => Platform.TouchUi
        ? new(HudAnchor.Spot.TopRight,
              new Vector2(TouchEdge, TouchEdge + LauncherButtonSize * 1.32f), Vector2.Zero)
        : new(HudAnchor.Spot.MidRight, new Vector2(HudAnchor.Edge, 0f), Vector2.Zero);

    public static HudAnchor.Spot? StatusAnchor =>
        Platform.TouchUi ? HudAnchor.Spot.BottomCenter : null;

    public static float VitalsScale => 1f;

    public static float TargetScale => 1f;

    public static HudAnchor.Spot? TargetAnchor =>
        Platform.TouchUi ? HudAnchor.Spot.TopCenter : null;

    public static Vector2 TargetMargin => new(0f, TouchEdge);

    public static float TrackerFontScale => Platform.TouchUi ? 1.45f : 1f;

    public static float LauncherButtonSize => Platform.TouchUi ? 92f : 44f;

    public const float LauncherGlyphInset = 0.17f;
    private const float SideIconGlyphGap = 10f;

    private static float SideIconLeft => TouchEdge + LibreKO.MiniMap.MapSize + SideIconGlyphGap
                                         - LauncherButtonSize * LauncherGlyphInset;

    private const float VitalsGap = 2f;

    private static float BottomStrip => BottomInset + VitalsGap;

    public static Vector2 StatusMargin => new(0f, BottomStrip);

    public static float PageStripBottom => BottomInset + 6f;

    public static Slot Buffs(Vector2 vitalsSize, float barInset) => new(
        HudAnchor.Spot.BottomCenter,
        new Vector2(StatusMargin.X - vitalsSize.X * VitalsScale * 0.5f + barInset * VitalsScale,
                    BottomStrip + vitalsSize.Y * VitalsScale + BuffRowGap),
        new Vector2(0f, BuffChipSize));

    public static Slot Clock => Platform.TouchUi
        ? new(HudAnchor.Spot.BottomLeft, new Vector2(TouchEdge, BottomStrip), Vector2.Zero)
        : new(HudAnchor.Spot.TopRight,
              new Vector2(HudAnchor.Edge, HudAnchor.Edge), Vector2.Zero);

    public static Slot Stats(Vector2 pointerMargin) => Platform.TouchUi
        ? new(HudAnchor.Spot.BottomCenter, new Vector2(0f, ExpBarHeight + 2f), Vector2.Zero)
        : new(HudAnchor.Spot.BottomRight, pointerMargin, Vector2.Zero);

    public static HudAnchor.Spot ChatAnchor => Platform.TouchUi
        ? HudAnchor.Spot.TopLeft
        : HudAnchor.Spot.BottomLeft;

    public static Vector2 ChatMargin => Platform.TouchUi
        ? new Vector2(TouchEdge, TouchEdge + LibreKO.MiniMap.TotalHeight + TouchEdge)
        : new Vector2(HudAnchor.Edge, HudAnchor.Edge);
}
