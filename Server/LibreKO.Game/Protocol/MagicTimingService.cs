using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public enum MagicTimingVerdict
{
    Allowed,
    OnCooldown,
    AlreadyCasting,
    CastTooEarly,
    TooSoonAfterLastSkill
}

public interface IMagicTimingService
{
    MagicTimingVerdict CheckCasting(UserSession session, MagicData magic);

    MagicTimingVerdict CheckRelease(UserSession session, MagicData magic);

    void OnCastAccepted(UserSession session, MagicData magic);

    void OnReleaseAccepted(UserSession session, MagicData magic);

    void OnCastAborted(UserSession session, int skillId);
}

public sealed class MagicTimingService(TimeProvider? timeProvider = null) : IMagicTimingService
{
    private const int LatencyGraceMs = 250;
    private const int AbandonedCastGraceMs = 3000;
    private const int SkillBurstFloorMs = 250;
    private const int TenthsPerSecond = 10;
    private const int MillisecondsPerTenth = 1000 / TenthsPerSecond;
    private const int CooldownEntryPruneThreshold = 256;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public MagicTimingVerdict CheckCasting(UserSession session, MagicData magic)
    {
        var now = _time.GetUtcNow().UtcTicks;

        if (RemainingCooldownMs(session, magic.Id, now) > 0)
            return MagicTimingVerdict.OnCooldown;

        if (!ConsumesAnItem(magic))
        {
            if (magic.CastTime > 0 && IsCastInFlight(session, now))
                return MagicTimingVerdict.AlreadyCasting;

            if (MillisecondsSince(session.SkillBurstTicks, now) < SkillBurstFloorMs)
                return MagicTimingVerdict.TooSoonAfterLastSkill;
        }

        return MagicTimingVerdict.Allowed;
    }

    public MagicTimingVerdict CheckRelease(UserSession session, MagicData magic)
    {
        var now = _time.GetUtcNow().UtcTicks;

        if (session.CastingSkillId == magic.Id)
        {
            return now + LatencyGraceMs * TimeSpan.TicksPerMillisecond < session.CastReadyTicks
                ? MagicTimingVerdict.CastTooEarly
                : MagicTimingVerdict.Allowed;
        }

        if (IsReleaseOfTheAcceptedCast(session, magic, now))
            return MagicTimingVerdict.Allowed;

        return RemainingCooldownMs(session, magic.Id, now) > 0
            ? MagicTimingVerdict.OnCooldown
            : MagicTimingVerdict.Allowed;
    }

    public void OnCastAccepted(UserSession session, MagicData magic)
    {
        var now = _time.GetUtcNow().UtcTicks;
        ArmCooldown(session, magic, now);
        session.AcceptedCasts[magic.Id] = now;
        if (session.AcceptedCasts.Count > CooldownEntryPruneThreshold)
            PruneAcceptedCasts(session, now);

        if (ConsumesAnItem(magic))
            return;

        session.SkillBurstTicks = now;
        if (magic.CastTime <= 0)
            return;

        session.CastingSkillId = magic.Id;
        session.CastReadyTicks = now + magic.CastTime * MillisecondsPerTenth * TimeSpan.TicksPerMillisecond;
        session.CastExpireTicks = session.CastReadyTicks + AbandonedCastGraceMs * TimeSpan.TicksPerMillisecond;
    }

    public void OnReleaseAccepted(UserSession session, MagicData magic)
    {
        session.AcceptedCasts.TryRemove(magic.Id, out _);
        if (session.CastingSkillId == magic.Id)
            ClearCast(session);
        else
            ArmCooldown(session, magic, _time.GetUtcNow().UtcTicks);
    }

    public void OnCastAborted(UserSession session, int skillId)
    {
        if (session.CastingSkillId == skillId)
            ClearCast(session);

        session.AcceptedCasts.TryRemove(skillId, out _);
        session.SkillCooldowns.TryRemove(skillId, out _);
    }

    private void ArmCooldown(UserSession session, MagicData magic, long now)
    {
        if (magic.ReCastTime <= 0)
            return;

        session.SkillCooldowns[magic.Id] =
            now + magic.ReCastTime * (TimeSpan.TicksPerSecond / TenthsPerSecond);

        if (session.SkillCooldowns.Count > CooldownEntryPruneThreshold)
            PruneCooldowns(session, now);
    }

    private static bool IsReleaseOfTheAcceptedCast(UserSession session, MagicData magic, long now)
    {
        if (!session.AcceptedCasts.TryGetValue(magic.Id, out var accepted))
            return false;

        var windowMs = magic.CastTime * MillisecondsPerTenth + AbandonedCastGraceMs;
        if (MillisecondsSince(accepted, now) <= windowMs)
            return true;

        session.AcceptedCasts.TryRemove(magic.Id, out _);
        return false;
    }

    private static void PruneAcceptedCasts(UserSession session, long now)
    {
        foreach (var (skillId, accepted) in session.AcceptedCasts)
            if (MillisecondsSince(accepted, now) > AbandonedCastGraceMs * 10)
                session.AcceptedCasts.TryRemove(skillId, out _);
    }

    private static long RemainingCooldownMs(UserSession session, int skillId, long now)
    {
        if (!session.SkillCooldowns.TryGetValue(skillId, out var readyAt))
            return 0;

        var remaining = (readyAt - now) / TimeSpan.TicksPerMillisecond - LatencyGraceMs;
        if (remaining > 0)
            return remaining;

        session.SkillCooldowns.TryRemove(skillId, out _);
        return 0;
    }

    private static long MillisecondsSince(long ticks, long now) =>
        ticks == 0 ? long.MaxValue : (now - ticks) / TimeSpan.TicksPerMillisecond;

    private static bool IsCastInFlight(UserSession session, long now)
    {
        if (session.CastingSkillId == 0)
            return false;

        if (now < session.CastExpireTicks)
            return true;

        ClearCast(session);
        return false;
    }

    private static bool ConsumesAnItem(MagicData magic) => magic.UseItem != 0;

    private static void ClearCast(UserSession session)
    {
        session.CastingSkillId = 0;
        session.CastReadyTicks = 0;
        session.CastExpireTicks = 0;
    }

    private static void PruneCooldowns(UserSession session, long now)
    {
        foreach (var (skillId, readyAt) in session.SkillCooldowns)
            if (readyAt <= now)
                session.SkillCooldowns.TryRemove(skillId, out _);
    }
}
