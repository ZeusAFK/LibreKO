using System;

namespace LibreKO.Domain;

public enum CrowdMode
{
    Quality,
    Balanced,
    Boost,
}

public readonly record struct CrowdLimits(
    int StrangerFxPerSecond, float StrangerFxRange, int GatherFxPerSecond, float GatherFxRange, double HoverHz);

public static class CrowdModes
{
    public const int Unlimited = 0;
    private const float QualityGatherRange = 60f;
    private const double FullHoverHz = 30.0;
    private const double BoostHoverHz = 15.0;

    public static CrowdLimits Of(CrowdMode mode) => mode switch
    {
        CrowdMode.Quality => new(Unlimited, float.PositiveInfinity, Unlimited, QualityGatherRange, FullHoverHz),
        CrowdMode.Boost => new(40, 40f, 8, 25f, BoostHoverHz),
        _ => new(100, 60f, 20, 40f, FullHoverHz),
    };

    public static string Label(CrowdMode mode) => mode switch
    {
        CrowdMode.Quality => "High quality",
        CrowdMode.Boost => "FPS boost",
        _ => "Balanced",
    };
}

public sealed class RateBudget
{
    private double tokens;
    private double last = double.NaN;

    public bool Take(int perSecond, double now)
    {
        if (perSecond <= CrowdModes.Unlimited) return true;
        if (double.IsNaN(last))
        {
            last = now;
            tokens = perSecond;
        }
        tokens = Math.Min(perSecond, tokens + Math.Max(0.0, now - last) * perSecond);
        last = now;
        if (tokens < 1.0) return false;
        tokens -= 1.0;
        return true;
    }
}
