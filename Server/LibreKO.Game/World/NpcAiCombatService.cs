using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface INpcAiCombatService
{
    Task HandleFightingAsync(NpcInstance npc, long nowTicks);
    Task HandleHealingAsync(NpcInstance npc, long nowTicks);
    Task HandleCastingAsync(NpcInstance npc, long nowTicks);
    Task ExecuteAttackAsync(NpcInstance npc, UserSession target, long nowTicks);
    Task StartHealCastAsync(NpcInstance healer, NpcInstance target, long nowTicks);
}

public class NpcAiCombatService(
    SessionManager sessionManager,
    IGameDataService gameData,
    INpcAiTargetingService npcAiTargetingService,
    INpcAiMovementService npcAiMovementService,
    INpcAiMagicService npcAiMagicService,
    INpcAiDeathService npcAiDeathService,
    ICombatNotificationService combatNotificationService,
    ILogger<NpcAiCombatService> logger) : INpcAiCombatService
{
    public async Task HandleFightingAsync(NpcInstance npc, long nowTicks)
    {
        var target = sessionManager.GetByCharacterId(npc.TargetUserId);
        if (target == null || target.Hp <= 0 || target.ZoneId != npc.ZoneId)
        {
            npcAiTargetingService.LoseTarget(npc, nowTicks);
            return;
        }

        if (npcAiTargetingService.DistanceSq(npc, target) > npc.AttackDistance * npc.AttackDistance)
        {
            npc.State = NpcState.Attacking;
            npc.BeginTracing();
            await npcAiMovementService.MoveTowardTargetAsync(npc, target, nowTicks);
            return;
        }

        var attackDelayTicks = TimeSpan.FromMilliseconds(npc.AttackDelay).Ticks;
        if (nowTicks - npc.LastAttackTicks < attackDelayTicks)
            return;

        await ExecuteAttackAsync(npc, target, nowTicks);
    }

    public async Task HandleHealingAsync(NpcInstance npc, long nowTicks)
    {
        var target = sessionManager.Regions.GetNpc(npc.ActiveTargetId);
        if (target == null || !target.IsAlive || (float)target.Hp / Math.Max(1, target.MaxHp) >= 0.9f)
        {
            npc.State = NpcState.Standing;
            npc.ActiveSkillId = 0;
            npc.ActiveTargetId = 0;
            npc.HealTargetIsNpc = false;
            npc.IsMoving = false;
            npc.StateChangeTicks = nowTicks;
            if (npc.TargetUserId == 0 && npc.Hp >= npc.MaxHp)
                sessionManager.Regions.MarkNpcIdle(npc);
            return;
        }

        var magic = gameData.GetMagic(npc.Magic3);
        var magicRange = magic?.Range > 0 ? magic.Range : npc.SearchRange;
        if (npcAiTargetingService.DistanceSqToPoint(npc, target.X, target.Z) <= magicRange * magicRange)
        {
            await StartHealCastAsync(npc, target, nowTicks);
            return;
        }

        if (npcAiTargetingService.DistanceSqToPoint(npc, npc.SpawnX, npc.SpawnZ) > npc.TracingRange * npc.TracingRange)
        {
            npc.State = NpcState.Returning;
            npc.ActiveSkillId = 0;
            npc.ActiveTargetId = 0;
            npc.HealTargetIsNpc = false;
            npc.StateChangeTicks = nowTicks;
            return;
        }

        await npcAiMovementService.MoveTowardPointAsync(npc, target.X, target.Z, nowTicks);
    }

    public async Task HandleCastingAsync(NpcInstance npc, long nowTicks)
    {
        await npcAiMagicService.HandleCastingAsync(npc, nowTicks);
    }

    public async Task ExecuteAttackAsync(NpcInstance npc, UserSession target, long nowTicks)
    {
        npc.LastAttackTicks = nowTicks;

        if (npc.HasMagicAttack)
        {
            var magicChance = Random.Shared.Next(1, 10001);
            if (magicChance <= 3000 && await npcAiMagicService.TryStartAttackCastAsync(npc, target, nowTicks))
                return;
        }

        await ExecutePhysicalAttackAsync(npc, target, nowTicks);
    }

    public async Task StartHealCastAsync(NpcInstance healer, NpcInstance target, long nowTicks)
    {
        await npcAiMagicService.StartHealCastAsync(healer, target, nowTicks);
    }

    private async Task ExecutePhysicalAttackAsync(NpcInstance npc, UserSession target, long nowTicks)
    {
        var attackResult = AttackResult.Failed;

        logger.LogDebug(
            "NPC physical attack start: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} zone={ZoneId} state=\"{State}\" hp={TargetHp}/{TargetMaxHp} totalAc={TargetAc} totalEvasion={TargetEvasion} attack1={Attack1} attack2={Attack2} hitRate={HitRate}",
            npc.NpcId,
            npc.UniqueId,
            npc.Name,
            target.CharacterId,
            target.Name,
            npc.ZoneId,
            npc.State,
            target.Hp,
            target.MaxHp,
            target.Stats.TotalAc,
            target.Stats.TotalEvasionrate,
            npc.Attack1,
            npc.Attack2,
            npc.HitRate);

        if (!target.BlockPhysical)
        {
            var totalHit = npc.Attack1 > 0 ? npc.Attack1 : npc.Attack2;
            if (totalHit > 0)
            {
                var tempAc = target.Stats.TotalAc;
                if (tempAc < 0)
                    tempAc = 0;

                var hitBase = totalHit * 200 / (tempAc + 240);
                if (hitBase > 0)
                {
                    var npcHitrate = Math.Max(1f, npc.HitRate);
                    var rate = npcHitrate / Math.Max(1f, target.Stats.TotalEvasionrate);
                    var hitResult = CombatUtils.GetHitRate(rate);
                    if (hitResult != AttackHitResult.Fail)
                    {
                        var random = Random.Shared.Next(0, Math.Max(1, hitBase));
                        var damage = (int)(0.85f * hitBase + 0.3f * random);
                        if (hitResult == AttackHitResult.GreatSuccess)
                            damage = damage * 3 / 2;

                        var damageCap = (int)(2.6f * totalHit);
                        if (damage > damageCap)
                            damage = damageCap;

                        damage = Math.Clamp(damage, 0, CombatUtils.MaxDamage);
                        if (damage > 0)
                        {
                            target.Hp = (short)Math.Max(0, target.Hp - damage);
                            attackResult = target.Hp > 0 ? AttackResult.Succeeded : AttackResult.TargetDead;

                            logger.LogDebug(
                                "NPC physical attack applied: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} totalHit={TotalHit} tempAc={TempAc} hitBase={HitBase} hitRateRatio={HitRateRatio} hitResult={HitResult} damage={Damage} hpAfter={HpAfter}",
                                npc.NpcId,
                                npc.UniqueId,
                                npc.Name,
                                target.CharacterId,
                                target.Name,
                                totalHit,
                                tempAc,
                                hitBase,
                                rate,
                                hitResult,
                                damage,
                                target.Hp);

                            await combatNotificationService.SendHpChangeAsync(target, npc.UniqueId);
                        }
                        else
                        {
                            logger.LogDebug(
                                "NPC physical attack resolved to zero damage after clamp: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} totalHit={TotalHit} tempAc={TempAc} hitBase={HitBase} hitRateRatio={HitRateRatio} hitResult={HitResult}",
                                npc.NpcId,
                                npc.UniqueId,
                                npc.Name,
                                target.CharacterId,
                                target.Name,
                                totalHit,
                                tempAc,
                                hitBase,
                                rate,
                                hitResult);
                        }
                    }
                    else
                    {
                        logger.LogDebug(
                            "NPC physical attack missed: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} totalHit={TotalHit} tempAc={TempAc} hitBase={HitBase} hitRateRatio={HitRateRatio}",
                            npc.NpcId,
                            npc.UniqueId,
                            npc.Name,
                            target.CharacterId,
                            target.Name,
                            totalHit,
                            tempAc,
                            hitBase,
                            rate);
                    }
                }
                else
                {
                    logger.LogDebug(
                        "NPC physical attack fully mitigated by AC before hit roll: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} totalHit={TotalHit} tempAc={TempAc}",
                        npc.NpcId,
                        npc.UniqueId,
                        npc.Name,
                        target.CharacterId,
                        target.Name,
                        totalHit,
                        tempAc);
                }
            }
            else
            {
                logger.LogDebug(
                    "NPC physical attack skipped due to zero totalHit: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} attack1={Attack1} attack2={Attack2}",
                    npc.NpcId,
                    npc.UniqueId,
                    npc.Name,
                    target.CharacterId,
                    target.Name,
                    npc.Attack1,
                    npc.Attack2);
            }
        }
        else
        {
            logger.LogDebug(
                "NPC physical attack blocked: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName}",
                npc.NpcId,
                npc.UniqueId,
                npc.Name,
                target.CharacterId,
                target.Name);
        }

        var attackPacket = AttackPacketWriter.Create(
            AttackPacketWriter.TypeMelee, attackResult, npc.UniqueId, target.CharacterId);
        await sessionManager.Regions.SendToRegion(target, attackPacket, excludeSender: false);

        if (attackResult != AttackResult.TargetDead)
            return;

        await npcAiDeathService.HandlePlayerKilledByNpcAsync(target, npc);
        npcAiTargetingService.LoseTarget(npc, nowTicks);
    }
}
