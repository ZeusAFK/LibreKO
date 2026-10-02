namespace LibreKO.Domain;

public static class FrameLimit
{
    private const double RefreshTolerance = 0.5;

    public static int EngineCap(int cap, bool vsync, double refreshRate)
    {
        if (cap <= 0) return 0;
        if (vsync && refreshRate > 0 && cap >= refreshRate - RefreshTolerance) return 0;
        return cap;
    }
}
