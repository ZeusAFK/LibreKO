using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface INpcAiBehaviorService
{
    Task ProcessNpcAsync(NpcInstance npc, long nowTicks);
}

public class NpcAiBehaviorService(
    SessionManager sessionManager,
    INpcAiTargetingService npcAiTargetingService,
    INpcAiMovementService npcAiMovementService,
    INpcAiCombatService npcAiCombatService,
    IGuardSummonAiService guardSummonAiService,
    IPetAiService petAiService,
    IPetSkillService petSkillService,
    IFullMoonRiftService fullMoonRiftService) : INpcAiBehaviorService
{
    public async Task ProcessNpcAsync(NpcInstance npc, long nowTicks)
    {
        if (npc.Debuffs.IsStunned(nowTicks))
        {
            npc.IsMoving = false;
            return;
        }

        if (npc.IsGuardSummon)
        {
            await guardSummonAiService.TickAsync(npc, nowTicks);
            return;
        }

        if (npc.IsPet)
        {
            await petAiService.TickAsync(npc, nowTicks);
            await petSkillService.ResumeAsync(npc, nowTicks);
            return;
        }

        if (npc.TargetPetId != 0
            && npc.State is NpcState.Standing or NpcState.Attacking or NpcState.Fighting
            && await npcAiCombatService.FightFamiliarAsync(npc, nowTicks))
            return;

        switch (npc.State)
        {
            case NpcState.Standing:
                await HandleStandingAsync(npc, nowTicks);
                break;

            case NpcState.Attacking:
                await HandleChasingAsync(npc, nowTicks);
                break;

            case NpcState.Fighting:
                await npcAiCombatService.HandleFightingAsync(npc, nowTicks);
                break;

            case NpcState.Casting:
                await npcAiCombatService.HandleCastingAsync(npc, nowTicks);
                break;

            case NpcState.Returning:
                await npcAiMovementService.HandleReturningAsync(npc, nowTicks);
                break;

            case NpcState.Healing:
                await npcAiCombatService.HandleHealingAsync(npc, nowTicks);
                break;

            case NpcState.Moving:
                await npcAiMovementService.HandleIdleMovingAsync(npc, nowTicks);
                break;

            case NpcState.Sleeping:
                await HandleSleepingAsync(npc, nowTicks);
                break;
        }
    }

    private async Task HandleSleepingAsync(NpcInstance npc, long nowTicks)
    {
        if (nowTicks < npc.WakeTicks && npc.TargetUserId == 0)
            return;

        npc.WakeTicks = 0;
        npc.State = NpcState.Standing;
        npc.StateChangeTicks = nowTicks;

        await sessionManager.Regions.BroadcastFromNpc(
            npc,
            MovementPacketWriter.StateChange(
                npc.UniqueId, (byte)StateChangeType.Pose, (byte)NpcPoseState.Awake));

        var target = sessionManager.GetByCharacterId(npc.TargetUserId);
        if (target != null && target.Hp > 0 && target.ZoneId == npc.ZoneId)
            npc.EngageTarget(target, allowStateInterrupt: true);
    }

    private async Task HandleStandingAsync(NpcInstance npc, long nowTicks)
    {
        if (npc.IsHealer && npc.TargetUserId == 0)
        {
            var woundedAlly = npcAiTargetingService.FindWoundedAlly(npc);
            if (woundedAlly != null)
            {
                await npcAiCombatService.StartHealCastAsync(npc, woundedAlly, nowTicks);
                return;
            }
        }

        var target = npcAiTargetingService.FindTarget(npc);
        if (target == null)
        {
            if (npc.ZoneId == (byte)ZoneId.DungeonDefence && npc.IsMonster)
            {
                var tower = sessionManager.Regions.GetNearbyNpcsForNpc(npc)
                    .Where(n => (n.NpcId == FullMoonRules.NexusTowerNpcId || FullMoonRules.InitialTowers.Any(t => t.NpcId == n.NpcId)) && n.IsAlive)
                    .MinBy(n => npcAiTargetingService.DistanceSqToPoint(npc, n.X, n.Z));

                if (tower != null)
                {
                    var distSq = npcAiTargetingService.DistanceSqToPoint(npc, tower.X, tower.Z);
                    var maxAttackDist = Math.Max(3f, (float)npc.AttackDistance + 1f);
                    if (distSq <= maxAttackDist * maxAttackDist)
                    {
                        await StrikeTowerAsync(npc, tower, nowTicks);
                        return;
                    }

                    await npcAiMovementService.MoveTowardPointAsync(npc, tower.X, tower.Z, nowTicks);
                    return;
                }

                if (npc.MoveType != NpcMoveType.None && npc.MoveType != NpcMoveType.Stationary)
                {
                    sessionManager.Regions.MarkNpcEngaged(npc);
                    await npcAiMovementService.StartIdleMovementAsync(npc, nowTicks);
                }

                return;
            }

            if (npc.MoveType != NpcMoveType.None
                && npc.MoveType != NpcMoveType.Stationary
                && (npc.RollsToPathEnd || nowTicks - npc.StateChangeTicks > TimeSpan.TicksPerSecond * Random.Shared.Next(3, 6))
                && sessionManager.Regions.HasNearbyUsers(npc))
            {
                sessionManager.Regions.MarkNpcEngaged(npc);
                await npcAiMovementService.StartIdleMovementAsync(npc, nowTicks);
            }

            return;
        }

        npc.TargetUserId = target.CharacterId;
        npc.StateChangeTicks = nowTicks;
        sessionManager.Regions.MarkNpcEngaged(npc);
        npcAiTargetingService.CallFamilyAllies(npc, target.CharacterId);

        if (npcAiTargetingService.DistanceSq(npc, target) <= npc.AttackDistance * npc.AttackDistance)
        {
            npc.State = NpcState.Fighting;
            return;
        }

        npc.State = NpcState.Attacking;
        npc.BeginTracing();
        await npcAiMovementService.MoveTowardTargetAsync(npc, target, nowTicks);
    }

    private async Task HandleChasingAsync(NpcInstance npc, long nowTicks)
    {
        var target = sessionManager.GetByCharacterId(npc.TargetUserId);
        if (target == null || target.Hp <= 0 || target.ZoneId != npc.ZoneId)
        {
            npcAiTargetingService.LoseTarget(npc, nowTicks);
            return;
        }

        npc.BeginTracing();

        var chaseRange = npc.ChaseRange(npc.WasDamagedBy(target.CharacterId));
        var lostRange = chaseRange + NpcInstance.LostTargetDistance;
        if (npcAiTargetingService.DistanceSqToPoint(npc, npc.TracingStartX, npc.TracingStartZ) > chaseRange * chaseRange
            || npcAiTargetingService.DistanceSq(npc, target) > lostRange * lostRange)
        {
            npcAiTargetingService.LoseTarget(npc, nowTicks);
            return;
        }

        if (npcAiTargetingService.DistanceSq(npc, target) <= npc.AttackDistance * npc.AttackDistance)
        {
            npc.State = NpcState.Fighting;
            npc.IsMoving = false;
            await npcAiCombatService.ExecuteAttackAsync(npc, target, nowTicks);
            return;
        }

        await npcAiMovementService.MoveTowardTargetAsync(npc, target, nowTicks);
    }

    private async Task StrikeTowerAsync(NpcInstance monster, NpcInstance tower, long nowTicks)
    {
        if (nowTicks - monster.LastAttackTicks < TimeSpan.FromMilliseconds(monster.AttackDelay).Ticks)
            return;

        monster.LastAttackTicks = nowTicks;
        var damage = CombatUtils.NpcStrikeDamage(monster.TotalHit, tower.TotalAc, monster.HitRate, tower.EvadeRate);
        if (damage <= 0)
            damage = Math.Max(10, monster.TotalHit / 5);

        tower.Hp = Math.Max(0, tower.Hp - damage);
        var result = tower.Hp > 0 ? AttackResult.Succeeded : AttackResult.TargetDead;

        await sessionManager.Regions.BroadcastFromNpc(
            monster, AttackPacketWriter.Create(AttackPacketWriter.TypeMelee, result, monster.UniqueId, tower.UniqueId));

        if (result == AttackResult.TargetDead)
        {
            tower.DeathTimeTicks = DateTime.UtcNow.Ticks;
            tower.State = NpcState.Dead;
            tower.TargetUserId = 0;
            tower.TargetPetId = 0;
            tower.IsMoving = false;
            tower.WakeTicks = 0;
            sessionManager.Regions.MarkNpcIdle(tower);

            var deadPacket = DeathPacketWriter.NpcDeath(tower.UniqueId);
            await sessionManager.Regions.BroadcastFromNpc(tower, deadPacket);

            await fullMoonRiftService.OnNpcKilledAsync(tower, null);
        }
    }
}
