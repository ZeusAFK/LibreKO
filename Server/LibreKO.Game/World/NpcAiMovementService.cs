using LibreKO.Common.Infrastructure.Network;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public interface INpcAiMovementService
{
    Task StartIdleMovementAsync(NpcInstance npc, long nowTicks);
    Task HandleReturningAsync(NpcInstance npc, long nowTicks);
    Task HandleIdleMovingAsync(NpcInstance npc, long nowTicks);
    Task MoveTowardTargetAsync(NpcInstance npc, UserSession target, long nowTicks);
    Task MoveTowardPointAsync(NpcInstance npc, float targetX, float targetZ, long nowTicks);
}

public class NpcAiMovementService(
    SessionManager sessionManager,
    INpcAiTargetingService npcAiTargetingService,
    ILogger<NpcAiMovementService> logger) : INpcAiMovementService
{
    private const float SpawnReturnDistSq = 1.0f;
    private const float AiTickSeconds = NpcAiService.TickMs / 1000f;
    private const int NoProgressSeconds = 3;
    private const float ProgressEpsilon = 0.01f;

    public async Task StartIdleMovementAsync(NpcInstance npc, long nowTicks)
    {
        float targetX;
        float targetZ;

        switch (npc.MoveType)
        {
            case NpcMoveType.Wander:
                targetX = npc.SpawnX + Random.Shared.Next(-5, 6);
                targetZ = npc.SpawnZ + Random.Shared.Next(-5, 6);
                break;

            case NpcMoveType.PatrolLoop:
            case NpcMoveType.ScriptedPath:
                if (npc.Waypoints.Length == 0)
                    return;
                if (npc.CurrentWaypoint >= npc.Waypoints.Length)
                    npc.CurrentWaypoint = 0;

                var (X, Z) = npc.Waypoints[npc.CurrentWaypoint];
                targetX = X;
                targetZ = Z;
                break;

            case NpcMoveType.PatrolOnce:
                if (npc.Waypoints.Length == 0 || npc.CurrentWaypoint >= npc.Waypoints.Length)
                {
                    npc.MoveType = NpcMoveType.None;
                    return;
                }

                var oneShotWaypoint = npc.Waypoints[npc.CurrentWaypoint];
                targetX = oneShotWaypoint.X;
                targetZ = oneShotWaypoint.Z;
                break;

            default:
                return;
        }

        npc.State = NpcState.Moving;
        npc.StateChangeTicks = nowTicks;
        await MoveTowardPointAsync(npc, targetX, targetZ, nowTicks);
    }

    public async Task HandleReturningAsync(NpcInstance npc, long nowTicks)
    {
        if (npcAiTargetingService.DistanceSqToPoint(npc, npc.SpawnX, npc.SpawnZ) <= SpawnReturnDistSq)
        {
            npc.X = npc.SpawnX;
            npc.Y = npc.SpawnY;
            npc.Z = npc.SpawnZ;
            npc.IsMoving = false;
            npc.State = NpcState.Standing;
            npc.Hp = npc.MaxHp;
            npc.ForgetDamagers();
            sessionManager.Regions.UpdateNpcRegion(npc);
            sessionManager.Regions.MarkNpcIdle(npc);
            return;
        }

        var newTarget = npcAiTargetingService.FindAggressiveTarget(npc);
        if (newTarget != null)
        {
            npc.TargetUserId = newTarget.CharacterId;
            npc.State = NpcState.Attacking;
            npc.StateChangeTicks = nowTicks;
            npc.BeginTracing();
            return;
        }

        await MoveTowardPointAsync(npc, npc.SpawnX, npc.SpawnZ, nowTicks);
    }

    public async Task HandleIdleMovingAsync(NpcInstance npc, long nowTicks)
    {
        var target = npcAiTargetingService.FindTarget(npc);
        if (target != null)
        {
            npc.TargetUserId = target.CharacterId;
            npc.StateChangeTicks = nowTicks;
            npc.State = NpcState.Attacking;
            return;
        }

        var arrived = npcAiTargetingService.DistanceSqToPoint(npc, npc.TargetX, npc.TargetZ) <= 1.0f;
        var stalled = nowTicks - npc.StateChangeTicks > TimeSpan.TicksPerSecond * NoProgressSeconds;

        if (arrived || stalled)
        {
            if (arrived && NpcInstance.FollowsAPath(npc.MoveType))
            {
                npc.CurrentWaypoint++;
                if (npc.CurrentWaypoint >= npc.Waypoints.Length)
                {
                    if (npc.MoveType == NpcMoveType.PatrolOnce)
                        npc.MoveType = NpcMoveType.None;
                    else
                        npc.CurrentWaypoint = 0;
                }
            }

            npc.IsMoving = false;
            npc.State = NpcState.Standing;
            npc.StateChangeTicks = nowTicks;
            sessionManager.Regions.MarkNpcIdle(npc);
            return;
        }

        var startX = npc.X;
        var startZ = npc.Z;
        await MoveTowardPointAsync(npc, npc.TargetX, npc.TargetZ, nowTicks);
        if (MathF.Abs(npc.X - startX) > ProgressEpsilon || MathF.Abs(npc.Z - startZ) > ProgressEpsilon)
            npc.StateChangeTicks = nowTicks;
    }

    public Task MoveTowardTargetAsync(NpcInstance npc, UserSession target, long nowTicks)
    {
        var dx = target.X - npc.X;
        var dz = target.Z - npc.Z;
        var dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist <= 0.01f)
            return MoveTowardPointAsync(npc, target.X, target.Z, nowTicks);

        var desiredDistance = Math.Max(0f, npc.AttackDistance - 0.5f);
        if (desiredDistance <= 0 || dist <= desiredDistance)
            return MoveTowardPointAsync(npc, target.X, target.Z, nowTicks);

        var ratio = desiredDistance / dist;
        var approachX = target.X - dx * ratio;
        var approachZ = target.Z - dz * ratio;
        return MoveTowardPointAsync(npc, approachX, approachZ, nowTicks);
    }

    public async Task MoveTowardPointAsync(NpcInstance npc, float targetX, float targetZ, long nowTicks)
    {
        npc.TargetX = targetX;
        npc.TargetZ = targetZ;
        var startX = npc.X;
        var startZ = npc.Z;

        var dx = targetX - npc.X;
        var dz = targetZ - npc.Z;
        var dist = MathF.Sqrt(dx * dx + dz * dz);

        if (dist < 0.01f)
            return;

        var stepSize = GetStepSize(npc);
        var step = MathF.Min(stepSize, dist);
        var directX = npc.X + dx / dist * step;
        var directZ = npc.Z + dz / dist * step;
        var newX = directX;
        var newZ = directZ;

        if (sessionManager.Maps != null && !sessionManager.Maps.IsMovable(npc.ZoneId, newX, newZ))
        {
            var map = sessionManager.Maps.GetMap(npc.ZoneId);
            if (map == null)
            {
                logger.LogDebug(
                    "NPC movement blocked without map data: npc={NpcId}/{UniqueId} state={State} from=({FromX:F2},{FromZ:F2}) attempted=({AttemptX:F2},{AttemptZ:F2}) target=({TargetX:F2},{TargetZ:F2})",
                    npc.NpcId,
                    npc.UniqueId,
                    npc.State,
                    npc.X,
                    npc.Z,
                    directX,
                    directZ,
                    targetX,
                    targetZ);
                return;
            }

            var nextStep = NpcPathfinder.FindNextStep(map, npc.X, npc.Z, targetX, targetZ);
            if (!nextStep.HasValue)
            {
                logger.LogDebug(
                    "NPC movement has no path: npc={NpcId}/{UniqueId} state={State} from=({FromX:F2},{FromZ:F2}) target=({TargetX:F2},{TargetZ:F2}) attempted=({AttemptX:F2},{AttemptZ:F2})",
                    npc.NpcId,
                    npc.UniqueId,
                    npc.State,
                    npc.X,
                    npc.Z,
                    targetX,
                    targetZ,
                    directX,
                    directZ);

                logger.LogDebug(
                    "NPC movement falling back to direct step after path failure: npc={NpcId}/{UniqueId} state={State} from=({FromX:F2},{FromZ:F2}) direct=({DirectX:F2},{DirectZ:F2}) target=({TargetX:F2},{TargetZ:F2})",
                    npc.NpcId,
                    npc.UniqueId,
                    npc.State,
                    npc.X,
                    npc.Z,
                    directX,
                    directZ,
                    targetX,
                    targetZ);
            }
            else
            {
                var pathDx = nextStep.Value.x - npc.X;
                var pathDz = nextStep.Value.z - npc.Z;
                var pathDist = MathF.Sqrt(pathDx * pathDx + pathDz * pathDz);
                if (pathDist <= 0.01f)
                    return;

                step = MathF.Min(stepSize, pathDist);
                newX = npc.X + pathDx / pathDist * step;
                newZ = npc.Z + pathDz / pathDist * step;

                if (!sessionManager.Maps.IsMovable(npc.ZoneId, newX, newZ))
                {
                    logger.LogDebug(
                        "NPC movement remained blocked after path adjustment: npc={NpcId}/{UniqueId} state={State} from=({FromX:F2},{FromZ:F2}) adjusted=({AdjustX:F2},{AdjustZ:F2}) target=({TargetX:F2},{TargetZ:F2})",
                        npc.NpcId,
                        npc.UniqueId,
                        npc.State,
                        npc.X,
                        npc.Z,
                        newX,
                        newZ,
                        targetX,
                        targetZ);

                    logger.LogDebug(
                        "NPC movement falling back to direct step after blocked path adjustment: npc={NpcId}/{UniqueId} state={State} from=({FromX:F2},{FromZ:F2}) direct=({DirectX:F2},{DirectZ:F2}) target=({TargetX:F2},{TargetZ:F2})",
                        npc.NpcId,
                        npc.UniqueId,
                        npc.State,
                        npc.X,
                        npc.Z,
                        directX,
                        directZ,
                        targetX,
                        targetZ);

                    newX = directX;
                    newZ = directZ;
                }
            }
        }

        npc.X = newX;
        npc.Z = newZ;
        if (sessionManager.Maps != null)
            npc.Y = sessionManager.Maps.GetHeight(npc.ZoneId, newX, newZ);
        npc.IsMoving = true;

        sessionManager.Regions.UpdateNpcRegion(npc);
        var movedDistance = MathF.Sqrt((newX - startX) * (newX - startX) + (newZ - startZ) * (newZ - startZ));
        var moveRate = (ushort)Math.Clamp((int)MathF.Round((movedDistance / AiTickSeconds) * 10.0f), 0, ushort.MaxValue);

        var movePacket = NpcSpawnPacketWriter.Move(
            npc.UniqueId, npc.GetPosX, npc.GetPosZ, npc.GetPosY, moveRate);
        await sessionManager.Regions.BroadcastFromNpc(npc, movePacket);
    }

    private static float GetStepSize(NpcInstance npc)
    {
        var speed = npc.State is NpcState.Attacking or NpcState.Fighting or NpcState.Returning or NpcState.Healing
            ? npc.Speed2
            : npc.Speed1;
        if (speed <= 0)
            speed = npc.State is NpcState.Attacking or NpcState.Fighting or NpcState.Returning or NpcState.Healing
                ? (byte)7
                : (byte)2;

        return speed * AiTickSeconds;
    }
}
