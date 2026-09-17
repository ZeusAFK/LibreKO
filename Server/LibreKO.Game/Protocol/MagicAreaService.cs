using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public class MagicAreaService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    ICombatLifecycleService combatLifecycleService)
{
    public async Task ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType7Table, magic, skillId, out var type7Data))
        {
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return;
        }

        var targetChange = (MagicAreaTargetChange)type7Data.TargetChange;
        if (targetChange is not (MagicAreaTargetChange.Provoke or MagicAreaTargetChange.Sleep))
        {
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return;
        }

        foreach (var npc in GatherTargets(caster, type7Data, targetId, data))
        {
            if (targetChange == MagicAreaTargetChange.Sleep)
            {
                await PutToSleepAsync(npc, type7Data.Duration);
                continue;
            }

            combatLifecycleService.SetNpcAggro(npc, caster);

            if (type7Data.Damage == 0)
                continue;

            int damage = type7Data.Damage;
            npc.Hp = Math.Max(0, npc.Hp - damage);
            npc.RecordDamage(caster.CharacterId, damage, caster, id => sessionManager.GetByCharacterId(id));
            await combatLifecycleService.SendNpcTargetHpAsync(caster, npc, damage);
            if (npc.Hp <= 0)
                await combatLifecycleService.HandleNpcDeathAsync(npc, caster);
        }

        await sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                targetId,
                data),
            excludeSender: false);
    }

    private List<NpcInstance> GatherTargets(
        UserSession caster, MagicType7Data type7Data, int targetId, int[] data)
    {
        var targets = new List<NpcInstance>();

        if (type7Data.Radius <= 0)
        {
            var single = sessionManager.Regions.GetNpc(targetId);
            if (IsValidTarget(single, caster))
                targets.Add(single!);
            return targets;
        }

        var centerX = AreaCoordinate(data.Length > 0 ? data[0] : 0, caster.X);
        var centerZ = AreaCoordinate(data.Length > 2 ? data[2] : 0, caster.Z);
        var radiusSq = type7Data.Radius * type7Data.Radius;

        foreach (var npc in sessionManager.Regions.GetNearbyNpcs(caster))
        {
            if (!IsValidTarget(npc, caster))
                continue;

            var dx = npc.X - centerX;
            var dz = npc.Z - centerZ;
            if (dx * dx + dz * dz <= radiusSq)
                targets.Add(npc);
        }

        return targets;
    }

    private async Task PutToSleepAsync(NpcInstance npc, short duration)
    {
        if (duration <= 0)
            return;

        var now = DateTime.UtcNow.Ticks;
        npc.State = NpcState.Sleeping;
        npc.TargetUserId = 0;
        npc.IsMoving = false;
        npc.WakeTicks = now + duration * TimeSpan.TicksPerSecond;
        npc.StateChangeTicks = now;

        await sessionManager.Regions.BroadcastFromNpc(
            npc,
            MovementPacketWriter.StateChange(
                npc.UniqueId, (byte)StateChangeType.Pose, (byte)NpcPoseState.Asleep));
    }

    private static bool IsValidTarget(NpcInstance? npc, UserSession caster) =>
        npc is { IsAlive: true } && npc.ZoneId == caster.ZoneId
        && NpcHostility.IsAttackableBy(npc, caster);

    private static float AreaCoordinate(int value, float fallback) => value == 0 ? fallback : value;
}
