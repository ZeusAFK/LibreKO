using System.Collections.Generic;
using Godot;

namespace LibreKO.Domain;

public static class WindowStack
{
    public const int None = -1;

    public static int TopmostAt(IReadOnlyList<Rect2> frames, Vector2 point)
    {
        for (int i = frames.Count - 1; i >= 0; i--)
            if (frames[i].HasPoint(point)) return i;
        return None;
    }
}
