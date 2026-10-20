using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public class MagicRangedService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    ICombatLifecycleService combatLifecycleService)
{
    public async Task<bool> ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType2Table, magic, skillId, out var type2Data))
        {
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return false;
        }

        var finalDamage = 0;
        var target = sessionManager.GetByCharacterId(targetId);
        if (target != null)
        {
            if (!PvpRules.CanAttackPlayer(caster, target))
            {
                await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                return false;
            }

            var items = target.BlockPhysical
                ? default
                : ItemCombatBonus.Of(caster, ElementResistances.Of(target.Stats), gameDataService);
            var skillDamage = target.BlockPhysical ? 0 : CalculateRangedDamage(
                caster,
                type2Data,
                target.Stats.TotalAc,
                target.Stats.TotalEvasionrate,
                isPlayerTarget: true);
            finalDamage = skillDamage + items.Element / ItemCombatBonus.PlayerSkillElementDivisor;
            if (finalDamage > 0)
                finalDamage = CombatUtils.ApplyWeaponTypeResistance(finalDamage, caster, target, gameDataService);

            finalDamage = GmMode.Taken(target, GmMode.Dealt(caster, target.Hp,
                Math.Min(finalDamage, CombatUtils.MaxDamage)));
            if (finalDamage > 0)
            {
                target.Hp = (short)Math.Max(0, target.Hp - finalDamage);
                target.MarkCombat();
                await combatLifecycleService.SendHpChangeAsync(target);
                await combatLifecycleService.SendPlayerTargetHpAsync(caster, target, finalDamage);
                if (target.Hp <= 0)
                    await combatLifecycleService.HandlePlayerDeathAsync(target, caster);
            }

            await ItemHitEffects.ApplyAsync(caster, target, items, skillDamage + items.Element, combatLifecycleService, gameDataService);
        }
        else
        {
            var npcTarget = sessionManager.Regions.GetNpc(targetId);
            if (npcTarget != null && npcTarget.IsAlive && !NpcHostility.IsAttackableBy(npcTarget, caster))
            {
                await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                return false;
            }

            if (npcTarget == null || !npcTarget.IsAlive || npcTarget.ZoneId != caster.ZoneId)
            {
                await EchoAsync(caster, skillId, targetId, data, 0);
                return true;
            }

            combatLifecycleService.SetNpcAggro(npcTarget, caster);
            var items = ItemCombatBonus.Of(caster, ElementResistances.Of(npcTarget), gameDataService);
            var skillDamage = CalculateRangedDamage(
                caster,
                type2Data,
                npcTarget.TotalAc,
                Math.Max(1f, npcTarget.EvadeRate),
                isPlayerTarget: false);
            finalDamage = skillDamage + items.Element;
            finalDamage = GmMode.Dealt(caster, npcTarget.Hp, Math.Min(finalDamage, CombatUtils.MaxDamage));
            if (finalDamage > 0)
            {
                npcTarget.Hp = Math.Max(0, npcTarget.Hp - finalDamage);
                npcTarget.RecordDamage(caster.CharacterId, finalDamage, caster, id => sessionManager.GetByCharacterId(id));
                await combatLifecycleService.SendNpcTargetHpAsync(caster, npcTarget, finalDamage);
                if (npcTarget.Hp <= 0)
                    await combatLifecycleService.HandleNpcDeathAsync(npcTarget, caster);
            }

            await ItemHitEffects.ApplyAsync(caster, null, items, skillDamage + items.Element, combatLifecycleService, gameDataService);
        }

        await EchoAsync(caster, skillId, targetId, data, finalDamage);
        return true;
    }

    private async Task EchoAsync(UserSession caster, int skillId, int targetId, int[] data, int finalDamage)
    {
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

    private static int CalculateRangedDamage(
        UserSession caster,
        MagicType2Data type2Data,
        int targetAc,
        float targetEvasion,
        bool isPlayerTarget)
    {
        targetAc = Math.Max(0, targetAc);
        var tempAp = MagicCombatHelper.GetSkillAttackPower(caster, isPlayerTarget);
        var tempHitB = (tempAp * 200 / 100) / (targetAc + 240);

        AttackHitResult result;
        if (type2Data.HitType is 1 or 2)
        {
            result = type2Data.HitRate <= Random.Shared.Next(0, 101) ? AttackHitResult.Fail : AttackHitResult.Success;
        }
        else
        {
            var rate = (caster.Stats.TotalHitrate / Math.Max(1f, targetEvasion)) * (type2Data.HitRate / 100.0f);
            result = CombatUtils.GetHitRate(rate);
        }

        if (result == AttackHitResult.Fail)
            return 0;

        var tempHit = type2Data.HitType == 1
            ? (int)(tempAp * (type2Data.AddDamage / 100.0f) / MagicCombatHelper.PercentScale)
            : (int)(tempHitB * (type2Data.AddDamage / 100.0f));

        var random = Random.Shared.Next(0, Math.Max(1, tempHit));
        return (int)(tempHit * 0.6f + random + 0.99f);
    }
}
