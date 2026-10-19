namespace LibreKO.Domain;

public sealed class LoadingGlide
{
    public const double GlideSeconds = 0.35;

    private double from;
    private double to;
    private double startedAt;

    public double Target => to;

    public void Set(double value, double now)
    {
        value = System.Math.Clamp(value, 0, 1);
        double shown = At(now);
        if (value < shown)
        {
            from = to = value;
            startedAt = now;
            return;
        }
        from = shown;
        to = value;
        startedAt = now;
    }

    public double At(double now)
    {
        double t = GlideSeconds <= 0 ? 1 : System.Math.Clamp((now - startedAt) / GlideSeconds, 0, 1);
        double eased = 1 - (1 - t) * (1 - t);
        return from + (to - from) * eased;
    }

    public static int Percent(double value) => (int)System.Math.Round(System.Math.Clamp(value, 0, 1) * 100);
}
