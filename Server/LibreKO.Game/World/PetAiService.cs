using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface IPetAiService
{
    Task TickAsync(NpcInstance pet, long nowTicks);
}

public sealed class PetAiService(
    SessionManager sessionManager,
    IPetService petService,
    ICombatLifecycleService combatLifecycleService) : IPetAiService
{
    public const short AttackDelayMs = 1500;
    public const byte WalkSpeed = 4;
    public const byte RunSpeed = 9;
    public const short HitRate = 100;
    public const float FollowDistance = 1.5f;
    public const float TeleportDistance = 25f;
    public const float StrikeReach = 2f;
    public const float LeashDistance = 30f;
    public static readonly TimeSpan OwnerStrikeMemory = TimeSpan.FromSeconds(10);
    private const float TickSeconds = NpcAiService.TickMs / 1000f;
    private const float MoveRateScale = 10f;

    public async Task TickAsync(NpcInstance pet, long nowTicks)
    {
        var owner = sessionManager.GetByCharacterId(pet.OwnerCharId);
        if (owner?.Pet is not { } state || state.Npc != pet || !pet.IsAlive)
            return;

        await petService.TickAsync(owner, nowTicks);
        if (!pet.IsAlive || owner.Hp <= 0 || owner.ZoneId != pet.ZoneId)
            return;

        if (state.Mode == PetMode.Attack && ResolveTarget(owner, state, pet, nowTicks) is { } target)
        {
            await EngageAsync(owner, state, pet, target, nowTicks);
            return;
        }

        await FollowAsync(owner, pet);
    }

    private NpcInstance? ResolveTarget(UserSession owner, PetState state, NpcInstance pet, long nowTicks)
    {
        if (state.TargetNpcId == PetState.NoTarget
            && owner.LastStruckNpcId != 0
            && nowTicks - owner.LastStruckTicks <= OwnerStrikeMemory.Ticks)
            state.TargetNpcId = owner.LastStruckNpcId;

        if (state.TargetNpcId == PetState.NoTarget)
            return null;

        var target = sessionManager.Regions.GetNpc(state.TargetNpcId);
        if (target is { IsAlive: true, IsAttackable: true } && target.ZoneId == pet.ZoneId && target.Room == pet.Room
            && DistanceSq(owner.X, owner.Z, target.X, target.Z) <= LeashDistance * LeashDistance)
            return target;

        state.TargetNpcId = PetState.NoTarget;
        if (owner.LastStruckNpcId == target?.UniqueId)
            owner.LastStruckNpcId = 0;
        return null;
    }

    private async Task EngageAsync(UserSession owner, PetState state, NpcInstance pet, NpcInstance target, long nowTicks)
    {
        var distance = MathF.Sqrt(DistanceSq(pet.X, pet.Z, target.X, target.Z));
        if (distance > StrikeReach)
        {
            await StepTowardAsync(pet, target.X, target.Z, distance - StrikeReach * 0.5f, RunSpeed);
            return;
        }

        if (nowTicks - state.LastAttackTicks < TimeSpan.FromMilliseconds(pet.AttackDelay).Ticks)
            return;

        state.LastAttackTicks = nowTicks;
        var damage = CombatUtils.NpcStrikeDamage(pet.TotalHit, target.TotalAc, pet.HitRate, target.EvadeRate);
        var result = AttackResult.Failed;
        if (damage > 0)
        {
            target.Hp = Math.Max(0, target.Hp - damage);
            target.RecordDamage(owner.CharacterId, damage, owner, sessionManager.GetByCharacterId);
            target.PetDamage.AddOrUpdate(owner.CharacterId, damage, (_, total) => total + damage);
            await combatLifecycleService.SendNpcTargetHpAsync(owner, target, damage);
            await owner.Client.SendPacket(PetPacketWriter.TargetHp(
                target.UniqueId, target.MaxHp, target.Hp, (short)Math.Clamp(-damage, short.MinValue, 0)));
            result = target.Hp > 0 ? AttackResult.Succeeded : AttackResult.TargetDead;
        }

        await sessionManager.Regions.BroadcastFromNpc(
            pet, AttackPacketWriter.Create(AttackPacketWriter.TypeMelee, result, pet.UniqueId, target.UniqueId));

        if (result == AttackResult.TargetDead)
        {
            state.TargetNpcId = PetState.NoTarget;
            await combatLifecycleService.HandleNpcDeathAsync(target, owner);
        }
    }

    private async Task FollowAsync(UserSession owner, NpcInstance pet)
    {
        var distance = MathF.Sqrt(DistanceSq(pet.X, pet.Z, owner.X, owner.Z));
        if (distance <= FollowDistance)
        {
            pet.IsMoving = false;
            return;
        }

        if (distance > TeleportDistance)
        {
            await PlaceAsync(pet, owner.X - FollowDistance, owner.Z - FollowDistance, 0f);
            return;
        }

        await StepTowardAsync(pet, owner.X, owner.Z, distance - FollowDistance, distance > FollowDistance * 4 ? RunSpeed : WalkSpeed);
    }

    private Task StepTowardAsync(NpcInstance pet, float targetX, float targetZ, float wanted, byte speed)
    {
        var dx = targetX - pet.X;
        var dz = targetZ - pet.Z;
        var length = MathF.Sqrt(dx * dx + dz * dz);
        if (length <= float.Epsilon || wanted <= 0f)
            return Task.CompletedTask;

        var step = MathF.Min(wanted, speed * TickSeconds);
        return PlaceAsync(pet, pet.X + dx / length * step, pet.Z + dz / length * step, step);
    }

    private async Task PlaceAsync(NpcInstance pet, float x, float z, float moved)
    {
        pet.X = x;
        pet.Z = z;
        if (sessionManager.Maps != null)
            pet.Y = sessionManager.Maps.GetHeight(pet.ZoneId, x, z);
        pet.IsMoving = moved > 0f;
        sessionManager.Regions.UpdateNpcRegion(pet);

        var moveRate = (ushort)Math.Clamp((int)MathF.Round(moved / TickSeconds * MoveRateScale), 0, ushort.MaxValue);
        await sessionManager.Regions.BroadcastFromNpc(
            pet, NpcSpawnPacketWriter.Move(pet.UniqueId, pet.GetPosX, pet.GetPosZ, pet.GetPosY, moveRate));
    }

    private static float DistanceSq(float ax, float az, float bx, float bz)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz;
    }
}
