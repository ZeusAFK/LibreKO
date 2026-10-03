using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class EdgeDock
{
    public enum Side { Left, Right }

    public const float Gap = 6f;
    public const float StripMaxHeight = 100f;
    public const float NarrowObstacleWidth = 260f;
    private const float TallObstacleShare = 0.6f;
    private const float TopBandShare = 0.4f;
    private const float BottomBandShare = 0.5f;

    public readonly record struct Frame(Vector2 Screen, float Margin, float BottomInset)
    {
        public float BottomLimit => Screen.Y - BottomInset - Margin;
    }

    public static Vector2[] Place(Side side, IReadOnlyList<Vector2> sizes, IReadOnlyList<Rect2> hud, Frame frame)
    {
        var strips = new List<Rect2>();
        var tops = new List<Rect2>();
        var bottoms = new List<Rect2>();
        float half = frame.Screen.X * 0.5f;
        foreach (var rect in hud)
        {
            bool onSide = side == Side.Right ? rect.Position.X >= half : rect.End.X <= half;
            if (!onSide || rect.Size.Y > frame.Screen.Y * TallObstacleShare) continue;
            if (rect.Position.Y < frame.Screen.Y * TopBandShare)
            {
                if (rect.Size.Y <= StripMaxHeight && rect.Position.Y <= frame.Margin * 2f) strips.Add(rect);
                else tops.Add(rect);
            }
            else if (rect.Position.Y >= frame.Screen.Y * BottomBandShare) bottoms.Add(rect);
        }

        float stripFloor = frame.Margin;
        foreach (var strip in strips) stripFloor = Mathf.Max(stripFloor, strip.End.Y + Gap);

        var placed = new Vector2[sizes.Count];
        float inner = side == Side.Right ? frame.Screen.X - frame.Margin : frame.Margin;
        for (int i = 0; i < sizes.Count; i++)
        {
            Vector2 size = sizes[i];
            float edgeX = side == Side.Right ? inner - size.X : inner;
            Vector2 spot;
            if (Fits(edgeX, size, stripFloor, tops, bottoms, frame, out float top, out Rect2? blocker))
                spot = new Vector2(edgeX, top);
            else if (blocker is { } narrow && narrow.Size.X <= NarrowObstacleWidth
                     && Fits(Beside(side, narrow, size), size, stripFloor, tops, bottoms, frame, out float besideTop, out _))
                spot = new Vector2(Beside(side, narrow, size), besideTop);
            else
                spot = new Vector2(edgeX, frame.BottomLimit - size.Y);

            spot.X = Mathf.Floor(Mathf.Clamp(spot.X, 0f, Mathf.Max(0f, frame.Screen.X - size.X)));
            spot.Y = Mathf.Floor(Mathf.Clamp(spot.Y, 0f, Mathf.Max(0f, frame.Screen.Y - size.Y)));
            placed[i] = spot;
            inner = side == Side.Right ? Mathf.Min(inner, spot.X - Gap) : Mathf.Max(inner, spot.X + size.X + Gap);
        }
        return placed;
    }

    private static float Beside(Side side, Rect2 obstacle, Vector2 size) =>
        side == Side.Right ? obstacle.Position.X - Gap - size.X : obstacle.End.X + Gap;

    private static bool Fits(float x, Vector2 size, float stripFloor, List<Rect2> tops, List<Rect2> bottoms,
                             Frame frame, out float top, out Rect2? blocker)
    {
        top = stripFloor;
        blocker = null;
        foreach (var rect in tops)
        {
            if (!Overlaps(x, size.X, rect) || rect.End.Y + Gap <= top) continue;
            top = rect.End.Y + Gap;
            blocker = rect;
        }

        float bottom = frame.BottomLimit;
        foreach (var rect in bottoms)
            if (Overlaps(x, size.X, rect)) bottom = Mathf.Min(bottom, rect.Position.Y - Gap);
        return top + size.Y <= bottom;
    }

    private static bool Overlaps(float x, float width, Rect2 rect) =>
        x < rect.End.X && x + width > rect.Position.X;
}
