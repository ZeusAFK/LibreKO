using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public static class MovePace
{
    // The client runs at 4.5 m/s, scaled by its speed percent.
    public const float RunMetresPerSecond = 4.5f;
    public const float LeadSeconds = 1.5f;
    // Retail's speed check allowed a quarter over the class's speed.
    private const int TolerancePercent = 125;
    private const int PercentScale = 100;
    private const float CreditSeconds = 3f;
    private const float SlackMetres = 1f;
    private const byte HeldFlags = ControlFlags.Stunned | ControlFlags.Frozen;

    public static float TopSpeed(UserSession session, long nowTicks) =>
        (ControlStateService.Of(session, nowTicks).Flags & HeldFlags) != 0
            ? 0f
            : RunMetresPerSecond * session.SpeedAmount / PercentScale * TolerancePercent / PercentScale;

    public static bool TryStep(UserSession session, float x, float z, long nowTicks)
    {
        var top = TopSpeed(session, nowTicks);
        var elapsed = Math.Clamp((float)(nowTicks - session.MoveBudgetTicks) / TimeSpan.TicksPerSecond, 0f, CreditSeconds);
        var budget = Math.Min(session.MoveBudget + top * elapsed, top * CreditSeconds);
        session.MoveBudgetTicks = nowTicks;

        var dx = x - session.X;
        var dz = z - session.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance > budget + SlackMetres)
        {
            session.MoveBudget = budget;
            return false;
        }

        session.MoveBudget = Math.Max(0f, budget - distance);
        return true;
    }

    public static bool LeadsTooFar(UserSession session, float curX, float curZ, float willX, float willZ, long nowTicks)
    {
        var reach = TopSpeed(session, nowTicks) * LeadSeconds + SlackMetres;
        var dx = willX - curX;
        var dz = willZ - curZ;
        return dx * dx + dz * dz > reach * reach;
    }
}
