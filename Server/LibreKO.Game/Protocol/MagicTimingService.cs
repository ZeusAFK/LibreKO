using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public enum MagicTimingVerdict
{
    Allowed,
    OnCooldown,
    AlreadyCasting,
    CastTooEarly,
    TooSoonAfterLastSkill,
    NotCast
}

public interface IMagicTimingService
{
    MagicTimingVerdict CheckCasting(UserSession session, MagicData magic);

    MagicTimingVerdict CheckRelease(UserSession session, MagicData magic);

    MagicTimingVerdict CheckVolley(UserSession session);

    void OnCastAccepted(UserSession session, MagicData magic, bool armCooldown = true);

    void OnVolleyAccepted(UserSession session, MagicData magic, int arrows);
    void OnFlightLaunched(UserSession session, MagicData magic);
    void OnReleaseAccepted(UserSession session, MagicData magic, bool armCooldown = true);

    void OnCastAborted(UserSession session, int skillId);

    void OnReleaseRefused(UserSession session, MagicData magic);
}

public sealed class MagicTimingService(IGameDataService gameDataService, TimeProvider? timeProvider = null) : IMagicTimingService
{
    private const int LatencyGraceMs = 250;
    private const int AbandonedCastGraceMs = 3000;
    private const int SkillBurstFloorMs = 250;
    private const int RangedCommitMs = 400;
    private const int PotionSharedCooldownMs = 2000;
    private const byte PotionItemGroup = 9;
    private const byte MoralSelf = 1;
    private const int TenthsPerSecond = 10;
    private const int MillisecondsPerTenth = 1000 / TenthsPerSecond;
    private const int CooldownEntryPruneThreshold = 256;

    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;

    public MagicTimingVerdict CheckCasting(UserSession session, MagicData magic)
    {
        if (magic.CastTime <= 0)
            return MagicTimingVerdict.NotCast;

        var now = time.GetUtcNow().UtcTicks;

        if (RemainingCooldownMs(session, magic.Id, now) > 0)
            return MagicTimingVerdict.OnCooldown;

        if (IsPotion(magic) && PotionCooldownRunning(session, now))
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
        var now = time.GetUtcNow().UtcTicks;

        if (session.CastingSkillId == magic.Id)
        {
            return now + LatencyGraceMs * TimeSpan.TicksPerMillisecond < session.CastCommitTicks
                ? MagicTimingVerdict.CastTooEarly
                : MagicTimingVerdict.Allowed;
        }

        if (IsReleaseOfTheAcceptedCast(session, magic, now))
            return MagicTimingVerdict.Allowed;

        if (magic.CastTime > 0 && !HasInstantMagic(session))
            return MagicTimingVerdict.NotCast;

        if (RemainingCooldownMs(session, magic.Id, now) > 0 || (IsPotion(magic) && PotionCooldownRunning(session, now)))
            return MagicTimingVerdict.OnCooldown;

        return IsPacedRelease(magic) && now < session.InstantSkillReadyTicks - SkillBurstFloorMs * TimeSpan.TicksPerMillisecond
            ? MagicTimingVerdict.TooSoonAfterLastSkill
            : MagicTimingVerdict.Allowed;
    }

    public void OnCastAccepted(UserSession session, MagicData magic, bool armCooldown = true)
    {
        var now = time.GetUtcNow().UtcTicks;
        if (armCooldown)
            ArmCooldown(session, magic, now);
        session.AcceptedCasts[magic.Id] = now;
        if (session.AcceptedCasts.Count > CooldownEntryPruneThreshold)
            PruneAcceptedCasts(session, now);

        if (IsPotion(magic))
            session.LastPotionTicks = now;

        if (ConsumesAnItem(magic))
            return;

        session.SkillBurstTicks = now;
        if (magic.CastTime <= 0)
            return;

        session.CastingSkillId = magic.Id;
        session.CastLaunched = false;
        var castMs = magic.CastTime * MillisecondsPerTenth;
        session.CastReadyTicks = now + castMs * TimeSpan.TicksPerMillisecond;
        session.CastCommitTicks = now + CommitMs(magic) * TimeSpan.TicksPerMillisecond;
        session.CastExpireTicks = session.CastReadyTicks + AbandonedCastGraceMs * TimeSpan.TicksPerMillisecond;
    }

    public MagicTimingVerdict CheckVolley(UserSession session) =>
        time.GetUtcNow().UtcTicks + LatencyGraceMs * TimeSpan.TicksPerMillisecond < session.NextShotTicks
            ? MagicTimingVerdict.TooSoonAfterLastSkill
            : MagicTimingVerdict.Allowed;

    public void OnVolleyAccepted(UserSession session, MagicData magic, int arrows)
    {
        session.PendingArrowHits[magic.Id] = Math.Max(1, arrows);
        session.NextShotTicks = Math.Max(session.NextShotTicks, time.GetUtcNow().UtcTicks)
            + RangedCommitMs * TimeSpan.TicksPerMillisecond;
    }

    public void OnFlightLaunched(UserSession session, MagicData magic)
    {
        if (session.CastingSkillId != magic.Id)
            return;

        if (magic.PrimaryType == MagicSkillType.Ranged)
        {
            session.CastLaunched = true;
            return;
        }

        ClearCast(session);
    }

    public void OnReleaseAccepted(UserSession session, MagicData magic, bool armCooldown = true)
    {
        if (session.PendingArrowHits.TryGetValue(magic.Id, out var arrows) && arrows > 1)
        {
            session.PendingArrowHits[magic.Id] = arrows - 1;
            return;
        }

        session.PendingArrowHits.TryRemove(magic.Id, out _);
        var releasesAnAcceptedCast = session.AcceptedCasts.TryRemove(magic.Id, out _);
        if (!releasesAnAcceptedCast && IsPotion(magic))
            session.LastPotionTicks = time.GetUtcNow().UtcTicks;

        if (session.CastingSkillId == magic.Id)
        {
            ClearCast(session);
            return;
        }

        if (releasesAnAcceptedCast)
            return;

        var now = time.GetUtcNow().UtcTicks;
        if (armCooldown)
            ArmCooldown(session, magic, now);
        if (IsPacedRelease(magic))
            session.InstantSkillReadyTicks = Math.Max(session.InstantSkillReadyTicks, now) + SkillBurstFloorMs * TimeSpan.TicksPerMillisecond;
    }

    public void OnCastAborted(UserSession session, int skillId)
    {
        if (session.CastingSkillId != skillId)
            return;

        var launched = session.CastLaunched;
        ClearCast(session);
        if (launched)
            return;

        session.SkillBurstTicks = 0;
        session.PendingArrowHits.TryRemove(skillId, out _);
        session.AcceptedCasts.TryRemove(skillId, out _);
        session.SkillCooldowns.TryRemove(skillId, out _);
    }

    public void OnReleaseRefused(UserSession session, MagicData magic)
    {
        if (magic.PrimaryType == MagicSkillType.Ranged)
        {
            OnReleaseAccepted(session, magic);
            return;
        }

        if (session.CastingSkillId == magic.Id)
            ClearCast(session);
        session.AcceptedCasts.TryRemove(magic.Id, out _);
        // The client gives a failed skill its timer back unless the skill has a flight.
        if (magic.FlyingEffect > 0)
            return;

        session.SkillBurstTicks = 0;
        session.SkillCooldowns.TryRemove(magic.Id, out _);
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

        var sinceMs = MillisecondsSince(accepted, now);
        if (sinceMs > magic.CastTime * MillisecondsPerTenth + AbandonedCastGraceMs)
        {
            session.AcceptedCasts.TryRemove(magic.Id, out _);
            return false;
        }

        return sinceMs + LatencyGraceMs >= CommitMs(magic) || HasInstantMagic(session);
    }

    private static int CommitMs(MagicData magic)
    {
        var castMs = magic.CastTime * MillisecondsPerTenth;
        return magic.PrimaryType == MagicSkillType.Ranged ? Math.Min(castMs, RangedCommitMs) : castMs;
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

    // A skill released without a cast keeps one per burst floor, two at once.
    private static bool IsPacedRelease(MagicData magic) =>
        !ConsumesAnItem(magic) && magic.PrimaryType != MagicSkillType.Ranged;

    private static bool HasInstantMagic(UserSession session) =>
        session.ActiveBuffs.Values.Any(buff => buff.BuffType == BuffType.InstantMagic && !buff.IsExpired);

    private bool IsPotion(MagicData magic) =>
        magic.ItemGroup == PotionItemGroup
        && magic.Moral == MoralSelf
        && magic.PrimaryType == MagicSkillType.OverTime
        && MagicTypeLookup.TryResolve(gameDataService.MagicType3Table, magic, magic.Id, out var type3Data)
        && type3Data.FirstDamage > 0
        && (MagicDirectType)type3Data.DirectType switch
        {
            MagicDirectType.Health or MagicDirectType.Mana => ConsumesAnItem(magic),
            MagicDirectType.HealthPurchase or MagicDirectType.ManaPurchase => true,
            _ => false,
        };

    private static bool PotionCooldownRunning(UserSession session, long now) =>
        MillisecondsSince(session.LastPotionTicks, now) < PotionSharedCooldownMs - LatencyGraceMs;

    private static void ClearCast(UserSession session)
    {
        session.CastingSkillId = 0;
        session.CastReadyTicks = 0;
        session.CastExpireTicks = 0;
        session.CastLaunched = false;
    }

    private static void PruneCooldowns(UserSession session, long now)
    {
        foreach (var (skillId, readyAt) in session.SkillCooldowns)
            if (readyAt <= now)
                session.SkillCooldowns.TryRemove(skillId, out _);
    }
}
