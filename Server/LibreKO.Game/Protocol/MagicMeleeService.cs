using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public class MagicMeleeService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    ICombatLifecycleService combatLifecycleService,
    Microsoft.Extensions.Logging.ILogger<MagicMeleeService> logger)
{
    public async Task ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType1Table, magic, skillId, out var type1Data))
        {
            logger.LogWarning("Type1 TryResolve FAILED: {Name} skill={SkillId} etc={Etc} tableCount={Count}",
                caster.Name, skillId, magic.Etc, gameDataService.MagicType1Table.Count);
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return;
        }

        var finalDamage = 0;
        var target = sessionManager.GetByCharacterId(targetId);
        if (target != null)
        {
            if (!PvpRules.CanAttackPlayer(caster, target))
            {
                await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                return;
            }

            finalDamage = CalculateMeleeDamage(
                caster,
                type1Data,
                target.Stats.TotalAc,
                target.Stats.TotalEvasionrate,
                isPlayerTarget: true);
            if (finalDamage > 0)
                finalDamage = CombatUtils.ApplyWeaponTypeResistance(finalDamage, caster, target, gameDataService);
            if (!target.BlockPhysical)
                finalDamage += PlayerBonusDamage(type1Data.AddDamage, caster.ZoneId);

            finalDamage = Math.Min(finalDamage, CombatUtils.MaxDamage);
            if (finalDamage > 0)
            {
                target.Hp = (short)Math.Max(0, target.Hp - finalDamage);
                await combatLifecycleService.SendHpChangeAsync(target);
                await combatLifecycleService.SendPlayerTargetHpAsync(caster, target, finalDamage);
                if (target.Hp <= 0)
                    await combatLifecycleService.HandlePlayerDeathAsync(target, caster);
            }
        }
        else
        {
            var npcTarget = sessionManager.Regions.GetNpc(targetId);
            if (npcTarget == null || !npcTarget.IsAlive || npcTarget.ZoneId != caster.ZoneId
                || !NpcHostility.IsAttackableBy(npcTarget, caster))
            {
                logger.LogWarning("Type1 NPC target fail: {Name} skill={SkillId} targetId={TargetId} found={Found} alive={Alive} attackable={Attackable}",
                    caster.Name, skillId, targetId,
                    npcTarget != null, npcTarget?.IsAlive, npcTarget?.IsAttackable);
                await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                return;
            }

            combatLifecycleService.SetNpcAggro(npcTarget, caster);
            finalDamage = CalculateMeleeDamage(
                caster,
                type1Data,
                npcTarget.Ac,
                Math.Max(1f, npcTarget.EvadeRate),
                isPlayerTarget: false);
            finalDamage += type1Data.AddDamage;
            finalDamage = Math.Min(finalDamage, CombatUtils.MaxDamage);
            if (finalDamage > 0)
            {
                npcTarget.Hp = Math.Max(0, npcTarget.Hp - finalDamage);
                npcTarget.RecordDamage(caster.CharacterId, finalDamage, caster, id => sessionManager.GetByCharacterId(id));
                await combatLifecycleService.SendNpcTargetHpAsync(caster, npcTarget, finalDamage);
                if (npcTarget.Hp <= 0)
                    await combatLifecycleService.HandleNpcDeathAsync(npcTarget, caster);
            }
        }

        // Preserves client sData and sets sData[3] = miss indicator
        data[3] = (short)(finalDamage == 0 ? -100 : 0);
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

    private const int WarZoneBonusDivisor = 2;
    private const int PeaceZoneBonusDivisor = 3;

    private static int PlayerBonusDamage(short addDamage, byte zoneId) =>
        addDamage / (BattleZoneManager.IsBattleZone(zoneId) || BattleZoneManager.IsPvpZone(zoneId)
            ? WarZoneBonusDivisor
            : PeaceZoneBonusDivisor);

    private static int CalculateMeleeDamage(
        UserSession caster,
        MagicType1Data type1Data,
        int targetAc,
        float targetEvasion,
        bool isPlayerTarget)
    {
        targetAc = Math.Max(0, targetAc);
        var tempAp = MagicCombatHelper.GetSkillAttackPower(caster, isPlayerTarget);
        var tempHitB = (tempAp * 200 / 100) / (targetAc + 240);

        AttackHitResult result;
        if (type1Data.HitType != 0)
        {
            result = type1Data.HitRate <= Random.Shared.Next(0, 101) ? AttackHitResult.Fail : AttackHitResult.Success;
        }
        else
        {
            var rate = (caster.Stats.TotalHitrate / Math.Max(1f, targetEvasion)) * (type1Data.HitRate / 100.0f);
            result = CombatUtils.GetHitRate(rate);
        }

        if (result == AttackHitResult.Fail)
            return 0;

        var tempHit = (int)(tempHitB * (type1Data.Hit / 100.0f));
        var random = Random.Shared.Next(0, Math.Max(1, tempHit));
        return (int)(tempHit + 0.3f * random + 0.99f);
    }
}
