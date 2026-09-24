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
    private const int AreaTarget = -1;
    private const float AreaReach = 6f;
    private const short AreaStruckFlag = 1;

    public async Task ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType1Table, magic, skillId, out var type1Data))
        {
            logger.LogWarning("Type1 TryResolve FAILED: {Name} skill={SkillId} etc={Etc} tableCount={Count}",
                caster.Name, skillId, magic.Etc, gameDataService.MagicType1Table.Count);
            await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
            return;
        }

        int finalDamage;
        if (targetId == AreaTarget)
        {
            if (type1Data.HitType != 0)
            {
                await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                return;
            }

            finalDamage = await StrikeAreaAsync(caster, type1Data, data);
            if (data.Length > 1)
                data[1] = AreaStruckFlag;
        }
        else
        {
            var target = sessionManager.GetByCharacterId(targetId);
            if (target != null)
            {
                if (!PvpRules.CanAttackPlayer(caster, target))
                {
                    await MagicCombatHelper.SendMagicFailAsync(caster, skillId);
                    return;
                }

                finalDamage = await StrikePlayerAsync(caster, type1Data, target);
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

                finalDamage = await StrikeNpcAsync(caster, type1Data, npcTarget);
            }
        }

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

    private async Task<int> StrikeAreaAsync(UserSession caster, MagicType1Data type1Data, int[] data)
    {
        float centreX = data.Length > 0 && data[0] != 0 ? data[0] : caster.X;
        float centreZ = data.Length > 2 && data[2] != 0 ? data[2] : caster.Z;
        float reachSq = AreaReach * AreaReach;
        var total = 0;

        foreach (var npc in sessionManager.Regions.GetNearbyNpcs(caster).ToList())
        {
            if (!npc.IsAlive || npc.ZoneId != caster.ZoneId || !NpcHostility.IsAttackableBy(npc, caster))
                continue;
            if (!WithinReach(npc.X, npc.Z, centreX, centreZ, reachSq))
                continue;
            total += await StrikeNpcAsync(caster, type1Data, npc);
        }

        foreach (var player in sessionManager.Regions.GetNearbyUsers(caster).ToList())
        {
            if (player.CharacterId == caster.CharacterId || player.Hp <= 0 || !PvpRules.CanAttackPlayer(caster, player))
                continue;
            if (!WithinReach(player.X, player.Z, centreX, centreZ, reachSq))
                continue;
            total += await StrikePlayerAsync(caster, type1Data, player);
        }

        return total;
    }

    private static bool WithinReach(float x, float z, float centreX, float centreZ, float reachSq)
    {
        var dx = x - centreX;
        var dz = z - centreZ;
        return dx * dx + dz * dz <= reachSq;
    }

    private async Task<int> StrikePlayerAsync(UserSession caster, MagicType1Data type1Data, UserSession target)
    {
        var finalDamage = CalculateMeleeDamage(
            caster,
            type1Data,
            target.Stats.TotalAc,
            target.Stats.TotalEvasionrate,
            isPlayerTarget: true);
        if (finalDamage > 0)
            finalDamage = CombatUtils.ApplyWeaponTypeResistance(finalDamage, caster, target, gameDataService);
        if (!target.BlockPhysical)
            finalDamage += PlayerBonusDamage(type1Data.AddDamage, caster.ZoneId);

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

        return finalDamage;
    }

    private async Task<int> StrikeNpcAsync(UserSession caster, MagicType1Data type1Data, NpcInstance npcTarget)
    {
        combatLifecycleService.SetNpcAggro(npcTarget, caster);
        var finalDamage = CalculateMeleeDamage(
            caster,
            type1Data,
            npcTarget.Ac,
            Math.Max(1f, npcTarget.EvadeRate),
            isPlayerTarget: false);
        finalDamage += type1Data.AddDamage;
        finalDamage = GmMode.Dealt(caster, npcTarget.Hp, Math.Min(finalDamage, CombatUtils.MaxDamage));
        if (finalDamage > 0)
        {
            npcTarget.Hp = Math.Max(0, npcTarget.Hp - finalDamage);
            npcTarget.RecordDamage(caster.CharacterId, finalDamage, caster, id => sessionManager.GetByCharacterId(id));
            await combatLifecycleService.SendNpcTargetHpAsync(caster, npcTarget, finalDamage);
            if (npcTarget.Hp <= 0)
                await combatLifecycleService.HandleNpcDeathAsync(npcTarget, caster);
        }

        return finalDamage;
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
