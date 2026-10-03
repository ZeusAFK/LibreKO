using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public readonly record struct PetSkillRequest(byte Stage, int SkillId, int CasterId, int TargetId, int[] Data);

public interface IPetSkillService
{
    Task UseAsync(UserSession session, PetSkillRequest request);
}

public sealed class PetSkillService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IPetService petService,
    IPetAiService petAi,
    IMagicStatusEffectService statusEffects,
    ILogger<PetSkillService> logger) : IPetSkillService
{
    public const float MeleeReach = PetAiService.StrikeReach + 1f;
    private const int MissMarkerSlot = 3;
    private const int MissMarker = -100;
    private const int MillisecondsPerTenth = 100;
    private const int PercentScale = 100;

    public async Task UseAsync(UserSession session, PetSkillRequest request)
    {
        if (session.Pet is not { IsSummoned: true, Npc: { } pet } state || request.CasterId != pet.UniqueId)
            return;

        var magic = gameData.GetMagic(request.SkillId);
        if (magic == null
            || !PetSkills.Knows(magic.Id, magic.Skill, magic.SkillLevel, state.Record.Class, state.Record.Level))
        {
            await FailAsync(session, pet, request.SkillId);
            return;
        }

        switch ((MagicProcessOpcode)request.Stage)
        {
            case MagicProcessOpcode.Casting:
                if (!Ready(state, pet, magic, DateTime.UtcNow.Ticks))
                {
                    await FailAsync(session, pet, magic.Id);
                    return;
                }
                await sessionManager.Regions.BroadcastFromNpc(
                    pet, MagicProcessPacketWriter.CreateCasting(magic.Id, pet.UniqueId, request.TargetId, request.Data));
                break;
            case MagicProcessOpcode.Effecting:
                await EffectAsync(session, state, pet, magic, request);
                break;
            default:
                logger.LogDebug("Familiar skill stage {Stage} from {Name} ignored", request.Stage, session.Name);
                break;
        }
    }

    private async Task EffectAsync(UserSession owner, PetState state, NpcInstance pet, MagicData magic, PetSkillRequest request)
    {
        var now = DateTime.UtcNow.Ticks;
        if (!Ready(state, pet, magic, now))
        {
            await FailAsync(owner, pet, magic.Id);
            return;
        }

        bool used;
        if (magic.Id == PetSkills.DesignatedAttack)
            used = await OrderAttackAsync(owner, state, pet, magic, request);
        else
            used = magic.PrimaryType switch
            {
                MagicSkillType.Melee => await StrikeAsync(owner, state, pet, magic, request),
                MagicSkillType.Buff when magic.Moral == PetSkills.OwnerMoral => await BuffOwnerAsync(owner, magic, request),
                _ => false,
            };

        if (!used)
        {
            await FailAsync(owner, pet, magic.Id);
            return;
        }

        await SpendAsync(owner, state, pet, magic, now);
    }

    private async Task<bool> OrderAttackAsync(UserSession owner, PetState state, NpcInstance pet, MagicData magic, PetSkillRequest request)
    {
        if (HostileTarget(owner, pet, request.TargetId) is not { } target)
            return false;

        await EngageAsync(owner, state, target);
        await sessionManager.Regions.BroadcastFromNpc(
            pet, MagicProcessPacketWriter.CreateEffecting(magic.Id, pet.UniqueId, target.UniqueId, request.Data));
        return true;
    }

    private async Task<bool> StrikeAsync(UserSession owner, PetState state, NpcInstance pet, MagicData magic, PetSkillRequest request)
    {
        if (HostileTarget(owner, pet, request.TargetId) is not { } target
            || DistanceSq(pet.X, pet.Z, target.X, target.Z) > MeleeReach * MeleeReach
            || !MagicTypeLookup.TryResolve(gameData.MagicType1Table, magic, magic.Id, out var type1))
            return false;

        await EngageAsync(owner, state, target);
        var totalHit = pet.TotalHit * type1.Hit / PercentScale + type1.AddDamage;
        var result = await petAi.StrikeAsync(owner, pet, target, totalHit, sureHit: type1.HitType != 0);

        var data = (int[])request.Data.Clone();
        if (data.Length > MissMarkerSlot)
            data[MissMarkerSlot] = result == AttackResult.Failed ? MissMarker : 0;
        await sessionManager.Regions.BroadcastFromNpc(
            pet, MagicProcessPacketWriter.CreateEffecting(magic.Id, pet.UniqueId, target.UniqueId, data));

        if (result == AttackResult.TargetDead)
            await petAi.SettleKillAsync(owner, state, target);
        return true;
    }

    private async Task<bool> BuffOwnerAsync(UserSession owner, MagicData magic, PetSkillRequest request)
    {
        if (owner.Hp <= 0)
            return false;

        await statusEffects.ExecuteAsync(owner, magic, MagicSkillType.Buff, magic.Id, owner.CharacterId, request.Data);
        return true;
    }

    private async Task EngageAsync(UserSession owner, PetState state, NpcInstance target)
    {
        if (state.Mode != PetMode.Attack)
            await petService.SetModeAsync(owner, PetMode.Attack);
        state.TargetNpcId = target.UniqueId;
    }

    private async Task SpendAsync(UserSession owner, PetState state, NpcInstance pet, MagicData magic, long now)
    {
        state.SkillReadyTicks[magic.Id] = now + TimeSpan.FromMilliseconds(magic.ReCastTime * MillisecondsPerTenth).Ticks;
        if (magic.Msp > 0)
        {
            pet.Mp = Math.Max(0, pet.Mp - magic.Msp);
            state.Record.Mp = (short)pet.Mp;
            await owner.Client.SendPacket(PetPacketWriter.MpChanged((short)pet.MaxMp, (short)pet.Mp, pet.UniqueId));
        }

        await petService.ChangeSatisfactionAsync(owner, (short)-PetSkills.SatisfactionPerSkill);
    }

    private static bool Ready(PetState state, NpcInstance pet, MagicData magic, long now) =>
        pet.Mp >= magic.Msp
        && (!state.SkillReadyTicks.TryGetValue(magic.Id, out var readyAt) || readyAt <= now);

    private NpcInstance? HostileTarget(UserSession owner, NpcInstance pet, int targetId) =>
        sessionManager.Regions.GetNpc(targetId) is { IsAlive: true } target
        && target.ZoneId == pet.ZoneId
        && target.Room == pet.Room
        && !target.IsPet
        && NpcHostility.IsAttackableBy(target, owner)
            ? target
            : null;

    private static Task FailAsync(UserSession owner, NpcInstance pet, int skillId) =>
        owner.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, pet.UniqueId));

    private static float DistanceSq(float ax, float az, float bx, float bz)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz;
    }
}
