using System;

namespace LibreKO.Domain;

public static class LookVariant
{
    public static bool CanStep(int count) => count > 1;

    public static int Clamp(int value, int count) => count > 0 ? Math.Clamp(value, 0, count - 1) : value;

    public static int Step(int value, int direction, int count)
    {
        if (!CanStep(count)) return value;
        int next = (Clamp(value, count) + direction) % count;
        return next < 0 ? next + count : next;
    }
}
