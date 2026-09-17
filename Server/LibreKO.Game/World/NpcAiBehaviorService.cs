using LibreKO.Common.Enums;
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
    INpcAiCombatService npcAiCombatService) : INpcAiBehaviorService
{
    public async Task ProcessNpcAsync(NpcInstance npc, long nowTicks)
    {
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
            if (npc.MoveType != NpcMoveType.None
                && npc.MoveType != NpcMoveType.Stationary
                && nowTicks - npc.StateChangeTicks > TimeSpan.TicksPerSecond * Random.Shared.Next(3, 6)
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
}
