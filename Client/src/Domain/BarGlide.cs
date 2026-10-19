namespace LibreKO.Domain;

public sealed class BarGlide
{
    public const float GlidePerSecond = 1.6f;
    public const float TrailPerSecond = 0.6f;
    public const double TrailHoldSeconds = 0.4;

    private float target;
    private double hold;

    public float Shown { get; private set; }
    public float Trail { get; private set; }

    public void Snap(float fraction)
    {
        Shown = Trail = target = fraction;
        hold = 0;
    }

    public void Aim(float fraction)
    {
        if (fraction < Shown)
        {
            if (Trail < Shown) Trail = Shown;
            hold = TrailHoldSeconds;
        }
        target = fraction;
    }

    public bool Step(double seconds)
    {
        float step = (float)(GlidePerSecond * seconds);
        Shown = target > Shown ? System.Math.Min(target, Shown + step) : System.Math.Max(target, Shown - step);
        if (Trail < Shown) Trail = Shown;
        if (hold > 0) hold -= seconds;
        else if (Trail > Shown) Trail = System.Math.Max(Shown, Trail - (float)(TrailPerSecond * seconds));
        return Shown != target || Trail != Shown;
    }
}
