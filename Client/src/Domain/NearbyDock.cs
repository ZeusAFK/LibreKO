using System;

namespace LibreKO.Domain;

public static class NearbyDock
{
    public const float Width = 200f;
    public const float Gap = 4f;

    public static int RowsThatFit(float height, float rowHeight, float separation) =>
        Math.Max(1, (int)((height + separation) / (rowHeight + separation)));

    public static int ClampFirst(int first, int count, int visible) =>
        Math.Clamp(first, 0, Math.Max(0, count - visible));
}
