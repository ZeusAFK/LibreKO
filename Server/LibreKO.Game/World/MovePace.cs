using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.World;

public static class MovePace
{
    // The client runs at 4.5 m/s, scaled by its speed percent, and stops only for Freeze and Stun.
    public const float RunMetresPerSecond = 4.5f;
    public const float ReportSeconds = 1.5f;
    public const float LeadSeconds = 1.5f;
    public const float LateReportSeconds = 0.5f;
    private const int TolerancePercent = 125;
    private const int PercentScale = 100;
    private const float CreditSeconds = 3f;
    private const float SlackMetres = 1f;
    private const float HoldNoticeSeconds = 0.5f;

    public static bool IsHeld(UserSession session, long nowTicks) => HeldSince(session, nowTicks) != null;

    public static float TopSpeed(UserSession session, long nowTicks) =>
        IsHeld(session, nowTicks) ? 0f : RunSpeed(session.SpeedAmount);

    public static bool TryStep(UserSession session, float x, float z, long nowTicks)
    {
        var fullRun = RunSpeed(Math.Max(session.SpeedAmount, (byte)PercentScale));
        float budget;
        if (HeldSince(session, nowTicks) is { } heldSince)
        {
            var noticed = heldSince + (long)(HoldNoticeSeconds * TimeSpan.TicksPerSecond);
            var runningSince = session.MoveOldEcho != WorldMovementService.MoveEchoFinish
                ? Math.Max(session.MoveBudgetTicks, heldSince - (long)((ReportSeconds + LateReportSeconds) * TimeSpan.TicksPerSecond))
                : heldSince;
            budget = Math.Min(session.MoveBudget, 0f) + fullRun * Seconds(Math.Min(nowTicks, noticed) - runningSince);
        }
        else
        {
            var run = RunSpeed(session.SpeedAmount);
            budget = Math.Min(session.MoveBudget + run * Seconds(nowTicks - session.MoveBudgetTicks), run * CreditSeconds);
        }
        session.MoveBudgetTicks = nowTicks;

        var dx = x - session.X;
        var dz = z - session.Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance > budget + SlackMetres)
        {
            session.MoveBudget = budget;
            return false;
        }

        session.MoveBudget = budget - distance;
        return true;
    }

    public static void Placed(UserSession session, long nowTicks)
    {
        session.PlacedTicks = nowTicks;
        session.MoveBudget = 0f;
        session.MoveBudgetTicks = nowTicks;
    }

    public static bool LeadsTooFar(UserSession session, float curX, float curZ, float willX, float willZ, long nowTicks)
    {
        var reach = TopSpeed(session, nowTicks) * LeadSeconds + SlackMetres;
        var dx = willX - curX;
        var dz = willZ - curZ;
        return dx * dx + dz * dz > reach * reach;
    }

    private static long? HeldSince(UserSession session, long nowTicks)
    {
        long? since = null;
        foreach (var buff in session.ActiveBuffs.Values)
        {
            if (buff.BuffType is not (BuffType.Freeze or BuffType.Stun) || buff.ExpireTicks <= nowTicks)
                continue;
            var start = buff.ExpireTicks - buff.Duration * TimeSpan.TicksPerSecond;
            since = since is { } earlier ? Math.Min(earlier, start) : start;
        }

        if (since is not { } held)
            return null;
        if (session.HoldSinceTicks != 0 && session.HoldUntilTicks > nowTicks)
            held = Math.Min(held, session.HoldSinceTicks);
        return Math.Min(held, nowTicks);
    }

    private static float Seconds(long ticks) => Math.Clamp((float)ticks / TimeSpan.TicksPerSecond, 0f, CreditSeconds);

    private static float RunSpeed(byte speedAmount) =>
        RunMetresPerSecond * speedAmount / PercentScale * TolerancePercent / PercentScale;
}
