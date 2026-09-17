using LibreKO.Game.Configuration;
using Microsoft.Extensions.Options;

using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public interface INpcAiTargetingService
{
    UserSession? FindTarget(NpcInstance npc);
    UserSession? FindAggressiveTarget(NpcInstance npc);
    void CallFamilyAllies(NpcInstance caller, int targetCharId);
    NpcInstance? FindWoundedAlly(NpcInstance healer);
    void LoseTarget(NpcInstance npc, long nowTicks);
    float DistanceSq(NpcInstance npc, UserSession player);
    float DistanceSqToPoint(NpcInstance npc, float x, float z);
}

public class NpcAiTargetingService(
    SessionManager sessionManager,
    IOptions<GameServerSettings> settings) : INpcAiTargetingService
{
    private const byte MoradonZoneId = (byte)ZoneId.Moradon;
    private const float SpawnReturnDistSq = 1.0f;
    private readonly float _closeAggroRange = Math.Max(0f, settings.Value.Monsters.CloseAggroRange);

    public UserSession? FindTarget(NpcInstance npc)
    {
        if (npc.IsScarecrow)
        {
            npc.TargetUserId = 0;
            return null;
        }

        if (npc.IsMonster || npc.IsGuard)
            return FindAggressiveTarget(npc);

        if (npc.TargetUserId > 0)
        {
            var damageTarget = sessionManager.GetByCharacterId(npc.TargetUserId);
            if (damageTarget != null && damageTarget.Hp > 0 && damageTarget.ZoneId == npc.ZoneId)
                return damageTarget;

            npc.TargetUserId = 0;
        }

        return null;
    }

    public UserSession? FindAggressiveTarget(NpcInstance npc)
    {
        if (npc.IsScarecrow)
            return null;

        if (npc.IsGuard && npc.ZoneId == MoradonZoneId)
            return null;

        var isGuard = npc.IsGuard;
        var searchRangeSq = npc.SearchRange * npc.SearchRange;
        var noticeRange = isGuard || npc.IsAggressive
            ? npc.SearchRange
            : MathF.Min(npc.SearchRange, _closeAggroRange);
        var noticeRangeSq = noticeRange * noticeRange;

        UserSession? closest = null;
        var closestDist = float.MaxValue;

        foreach (var player in sessionManager.Regions.GetNearbyUsersForNpc(npc))
        {
            if (player.Hp <= 0 || player.IsGM || player.ZoneId != npc.ZoneId || player.IsInvisible)
                continue;

            if (!NpcHostility.IsAttackableBy(npc, player))
                continue;

            var dist = DistanceSq(npc, player);
            if (dist >= closestDist)
                continue;

            var remembers = (npc.TopDamagerCharId != 0 && npc.WasDamagedBy(player.CharacterId))
                || (npc.HasFriends && npc.TargetUserId == player.CharacterId);
            if (dist >= (remembers ? searchRangeSq : noticeRangeSq))
                continue;

            closestDist = dist;
            closest = player;
        }

        return closest;
    }

    public void CallFamilyAllies(NpcInstance caller, int targetCharId)
    {
        var isBoss = caller.IsBoss;
        if (!isBoss && !caller.IsGuard && !caller.HasFriends)
            return;

        var callRangeSq = caller.TracingRange * caller.TracingRange;

        foreach (var ally in sessionManager.Regions.GetNearbyNpcsForNpc(caller))
        {
            if (ally.UniqueId == caller.UniqueId || !ally.IsAlive)
                continue;

            if (!ally.HasAi || ally.IsScarecrow || !NpcWorldFilter.ShouldSpawnNormally(ally))
                continue;

            if (ally.TargetUserId > 0 && ally.State is NpcState.Fighting or NpcState.Attacking)
                continue;

            if (caller.IsGuard)
            {
                if (!ally.IsGuard || ally.Nation != caller.Nation || ally.Family != caller.Family)
                    continue;
            }
            else if (!isBoss && (!ally.HasFriends || ally.Family != caller.Family))
            {
                continue;
            }

            if (ally.State != NpcState.Standing && ally.State != NpcState.Moving)
                continue;

            if (DistanceSqToPoint(ally, caller.X, caller.Z) > callRangeSq)
                continue;

            ally.TargetUserId = targetCharId;
            ally.State = NpcState.Attacking;
            ally.StateChangeTicks = DateTime.UtcNow.Ticks;
            sessionManager.Regions.MarkNpcEngaged(ally);
        }
    }

    public NpcInstance? FindWoundedAlly(NpcInstance healer)
    {
        var searchRangeSq = healer.SearchRange * healer.SearchRange;
        NpcInstance? bestTarget = null;
        var lowestHpPct = 0.9f;

        foreach (var ally in sessionManager.Regions.GetNearbyNpcsForNpc(healer))
        {
            if (ally.UniqueId == healer.UniqueId || !ally.IsAlive || ally.ZoneId != healer.ZoneId)
                continue;

            if (healer.Family > 0 && ally.Family != healer.Family)
                continue;

            var hpPct = (float)ally.Hp / Math.Max(1, ally.MaxHp);
            if (hpPct >= lowestHpPct)
                continue;

            if (DistanceSqToPoint(healer, ally.X, ally.Z) > searchRangeSq)
                continue;

            lowestHpPct = hpPct;
            bestTarget = ally;
        }

        var selfHpPct = (float)healer.Hp / Math.Max(1, healer.MaxHp);
        if (selfHpPct < lowestHpPct)
            bestTarget = healer;

        return bestTarget;
    }

    public void LoseTarget(NpcInstance npc, long nowTicks)
    {
        npc.TargetUserId = 0;
        npc.IsMoving = false;
        npc.IsTracing = false;

        if (DistanceSqToPoint(npc, npc.SpawnX, npc.SpawnZ) > SpawnReturnDistSq)
        {
            npc.State = NpcState.Returning;
            npc.StateChangeTicks = nowTicks;
            return;
        }

        npc.State = NpcState.Standing;
        npc.StateChangeTicks = nowTicks;
        sessionManager.Regions.MarkNpcIdle(npc);
    }

    public float DistanceSq(NpcInstance npc, UserSession player)
    {
        var dx = npc.X - player.X;
        var dz = npc.Z - player.Z;
        return dx * dx + dz * dz;
    }

    public float DistanceSqToPoint(NpcInstance npc, float x, float z)
    {
        var dx = npc.X - x;
        var dz = npc.Z - z;
        return dx * dx + dz * dz;
    }
}
