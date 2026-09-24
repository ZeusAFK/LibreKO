using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface IGuardSummonAiService
{
    Task TickAsync(NpcInstance guard, long nowTicks);
}

public class GuardSummonAiService(
    SessionManager sessionManager,
    INpcAiCombatService npcAiCombatService,
    ICombatLifecycleService combatLifecycleService) : IGuardSummonAiService
{
    public async Task TickAsync(NpcInstance guard, long nowTicks)
    {
        if (!guard.IsAlive)
            return;

        var owner = sessionManager.GetByCharacterId(guard.OwnerCharId);
        if (owner == null || owner.Hp <= 0 || owner.ZoneId != guard.ZoneId)
            return;

        if (nowTicks - guard.LastAttackTicks < TimeSpan.FromMilliseconds(guard.AttackDelay).Ticks)
            return;

        var reachSq = guard.AttackDistance * guard.AttackDistance;
        if (NearestEnemyPlayer(guard, owner, reachSq) is { } player)
        {
            await npcAiCombatService.ExecuteAttackAsync(guard, player, nowTicks);
            return;
        }

        if (NearestMonster(guard, reachSq) is { } monster)
            await StrikeMonsterAsync(guard, owner, monster, nowTicks);
    }

    private UserSession? NearestEnemyPlayer(NpcInstance guard, UserSession owner, float reachSq)
    {
        UserSession? best = null;
        var bestSq = reachSq;
        foreach (var player in sessionManager.Regions.GetNearbyUsersForNpc(guard))
        {
            if (player.IsGM || player.IsInvisible || !PvpRules.CanAttackPlayer(owner, player))
                continue;

            var distSq = DistanceSq(guard, player.X, player.Z);
            if (distSq > bestSq)
                continue;

            bestSq = distSq;
            best = player;
        }

        return best;
    }

    private NpcInstance? NearestMonster(NpcInstance guard, float reachSq)
    {
        NpcInstance? best = null;
        var bestSq = reachSq;
        foreach (var npc in sessionManager.Regions.GetNearbyNpcsForNpc(guard))
        {
            if (npc.UniqueId == guard.UniqueId || !npc.IsAlive || !npc.IsAttackable || npc.IsScarecrow
                || npc.ZoneId != guard.ZoneId)
                continue;

            var distSq = DistanceSq(guard, npc.X, npc.Z);
            if (distSq > bestSq)
                continue;

            bestSq = distSq;
            best = npc;
        }

        return best;
    }

    private async Task StrikeMonsterAsync(NpcInstance guard, UserSession owner, NpcInstance monster, long nowTicks)
    {
        guard.LastAttackTicks = nowTicks;

        var damage = CombatUtils.NpcStrikeDamage(
            guard.Attack1 > 0 ? guard.Attack1 : guard.Attack2, monster.Ac, guard.HitRate, monster.EvadeRate);
        var result = AttackResult.Failed;
        if (damage > 0)
        {
            monster.Hp = Math.Max(0, monster.Hp - damage);
            monster.RecordDamage(owner.CharacterId, damage, owner, sessionManager.GetByCharacterId);
            await combatLifecycleService.SendNpcTargetHpAsync(owner, monster, damage);
            result = monster.Hp > 0 ? AttackResult.Succeeded : AttackResult.TargetDead;
        }

        await sessionManager.Regions.BroadcastFromNpc(
            guard, AttackPacketWriter.Create(AttackPacketWriter.TypeMelee, result, guard.UniqueId, monster.UniqueId));

        if (result == AttackResult.TargetDead)
            await combatLifecycleService.HandleNpcDeathAsync(monster, owner);
    }

    private static float DistanceSq(NpcInstance guard, float x, float z)
    {
        var dx = guard.X - x;
        var dz = guard.Z - z;
        return dx * dx + dz * dz;
    }
}
