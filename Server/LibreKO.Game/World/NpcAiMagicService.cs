using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public interface INpcAiMagicService
{
    Task<bool> TryStartAttackCastAsync(NpcInstance npc, UserSession target, long nowTicks);
    Task StartHealCastAsync(NpcInstance healer, NpcInstance target, long nowTicks);
    Task HandleCastingAsync(NpcInstance npc, long nowTicks);
}

public class NpcAiMagicService(
    SessionManager sessionManager,
    IGameDataService gameData,
    INpcAiTargetingService npcAiTargetingService,
    INpcAiMovementService npcAiMovementService,
    INpcAiDeathService npcAiDeathService,
    ICombatNotificationService combatNotificationService,
    IUserNotificationService userNotificationService,
    ILogger<NpcAiMagicService> logger) : INpcAiMagicService
{
    private const byte MagicCasting = 1;
    private const byte MagicEffecting = 3;

    public async Task<bool> TryStartAttackCastAsync(NpcInstance npc, UserSession target, long nowTicks)
    {
        var magic = gameData.GetMagic(npc.Magic1);
        if (magic == null)
            return false;

        var magicRange = MagicRange(magic, npc);
        if (npcAiTargetingService.DistanceSq(npc, target) > magicRange * magicRange)
        {
            npc.State = NpcState.Attacking;
            npc.BeginTracing();
            await npcAiMovementService.MoveTowardTargetAsync(npc, target, nowTicks);
            return true;
        }

        npc.ActiveSkillId = npc.Magic1;
        npc.ActiveTargetId = target.CharacterId;
        npc.CastEndTicks = nowTicks + TimeSpan.FromMilliseconds(magic.CastTimeMs).Ticks;
        npc.State = NpcState.Casting;

        var castPacket = CreateMagicProcessPacket(MagicCasting, npc.Magic1, npc.UniqueId, target.CharacterId);
        await sessionManager.Regions.SendToRegion(target, castPacket, excludeSender: false);
        return true;
    }

    public async Task StartHealCastAsync(NpcInstance healer, NpcInstance target, long nowTicks)
    {
        var magic = gameData.GetMagic(healer.Magic3);
        if (magic == null)
            return;

        var magicRange = magic.Range > 0 ? magic.Range : healer.SearchRange;
        var distSq = npcAiTargetingService.DistanceSqToPoint(healer, target.X, target.Z);
        if (target.UniqueId != healer.UniqueId && distSq > magicRange * magicRange)
        {
            healer.State = NpcState.Healing;
            healer.ActiveTargetId = target.UniqueId;
            healer.HealTargetIsNpc = true;
            healer.ActiveSkillId = healer.Magic3;
            healer.StateChangeTicks = nowTicks;
            await npcAiMovementService.MoveTowardPointAsync(healer, target.X, target.Z, nowTicks);
            return;
        }

        healer.ActiveSkillId = healer.Magic3;
        healer.ActiveTargetId = target.UniqueId;
        healer.HealTargetIsNpc = true;
        healer.CastEndTicks = nowTicks + TimeSpan.FromMilliseconds(magic.CastTimeMs).Ticks;
        healer.State = NpcState.Casting;

        var castPacket = CreateMagicProcessPacket(MagicCasting, healer.Magic3, healer.UniqueId, target.UniqueId);
        await sessionManager.Regions.BroadcastFromNpc(healer, castPacket);
    }

    public async Task HandleCastingAsync(NpcInstance npc, long nowTicks)
    {
        if (nowTicks < npc.CastEndTicks)
            return;

        var magic = gameData.GetMagic(npc.ActiveSkillId);

        if (npc.HealTargetIsNpc)
        {
            var healTarget = sessionManager.Regions.GetNpc(npc.ActiveTargetId);
            ResetCastingState(npc, NpcState.Standing, nowTicks);

            if (healTarget != null && healTarget.IsAlive && magic != null)
                await ExecuteHealEffectAsync(npc, healTarget, magic);

            return;
        }

        var target = sessionManager.GetByCharacterId(npc.ActiveTargetId);
        if (target == null || target.Hp <= 0 || target.ZoneId != npc.ZoneId)
        {
            ResetCastingState(npc, NpcState.Fighting, nowTicks);
            npcAiTargetingService.LoseTarget(npc, nowTicks);
            return;
        }

        ResetCastingState(npc, NpcState.Fighting, nowTicks);

        if (magic == null)
            return;

        var magicRange = MagicRange(magic, npc);
        if (npcAiTargetingService.DistanceSq(npc, target) > magicRange * magicRange)
        {
            npc.State = NpcState.Attacking;
            npc.BeginTracing();
            return;
        }

        await ExecuteMagicEffectAsync(npc, target, magic, nowTicks);
    }

    private static float MagicRange(MagicData magic, NpcInstance npc)
        => magic.Range > 0 ? magic.Range : npc.AttackDistance;

    private async Task ExecuteMagicEffectAsync(NpcInstance npc, UserSession target, MagicData magic, long nowTicks)
    {
        if (magic.PrimaryType == MagicSkillType.Buff)
        {
            await ApplyNpcDebuffAsync(npc, target, magic);
            return;
        }

        var damage = magic.PrimaryType switch
        {
            MagicSkillType.Melee when gameData.MagicType1Table.TryGetValue(magic.Id, out var melee)
                => CalculateMeleeMagicDamage(npc, target, melee),
            MagicSkillType.Ranged when gameData.MagicType2Table.TryGetValue(magic.Id, out var ranged)
                => CalculateRangedMagicDamage(npc, target, ranged),
            MagicSkillType.OverTime when gameData.MagicType3Table.TryGetValue(magic.Id, out var overTime)
                => CalculateOverTimeMagicDamage(npc, overTime),
            _ => npc.Attack1 > 0 ? npc.Attack1 : npc.Attack2
        };

        logger.LogDebug(
            "NPC magic attack resolved: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} magicId={MagicId} type={MagicType} rawDamage={RawDamage} totalAc={TargetAc} attack1={Attack1} attack2={Attack2}",
            npc.NpcId,
            npc.UniqueId,
            npc.Name,
            target.CharacterId,
            target.Name,
            magic.Id,
            magic.PrimaryType,
            damage,
            target.Stats.TotalAc,
            npc.Attack1,
            npc.Attack2);

        damage = Math.Clamp(damage, 0, 32000);
        if (damage > 0)
            target.Hp = (short)Math.Max(0, target.Hp - damage);

        var effectPacket = CreateMagicProcessPacket(MagicEffecting, magic.Id, npc.UniqueId, target.CharacterId);
        await sessionManager.Regions.SendToRegion(target, effectPacket, excludeSender: false);

        if (damage > 0)
        {
            logger.LogDebug(
                "NPC magic attack applied: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} magicId={MagicId} damage={Damage} hpAfter={HpAfter}",
                npc.NpcId,
                npc.UniqueId,
                npc.Name,
                target.CharacterId,
                target.Name,
                magic.Id,
                damage,
                target.Hp);
            await combatNotificationService.SendHpChangeAsync(target, npc.UniqueId);
        }
        else
        {
            logger.LogDebug(
                "NPC magic attack dealt zero damage: npc={NpcId}/{UniqueId} npcName=\"{NpcName}\" target={TargetId}/{TargetName} magicId={MagicId}",
                npc.NpcId,
                npc.UniqueId,
                npc.Name,
                target.CharacterId,
                target.Name,
                magic.Id);
        }

        if (target.Hp > 0)
            return;

        await npcAiDeathService.HandlePlayerKilledByNpcAsync(target, npc);
        npcAiTargetingService.LoseTarget(npc, nowTicks);
    }

    private async Task ApplyNpcDebuffAsync(NpcInstance npc, UserSession target, MagicData magic)
    {
        if (!MagicTypeLookup.TryResolve(gameData.MagicType4Table, magic, magic.Id, out var type4Data))
            return;

        if (target.BlockCurses)
            return;

        if (target.ReflectCurses && Random.Shared.Next(100) < 25)
            return;

        if (magic.SuccessRate > 0 && magic.SuccessRate < 100 && Random.Shared.Next(0, 100) >= magic.SuccessRate)
            return;

        var buffType = (BuffType)type4Data.BuffType;
        var existingKey = target.ActiveBuffs
            .FirstOrDefault(entry => entry.Value.BuffType == buffType && buffType != BuffType.None).Key;
        if (existingKey > 0)
            target.ActiveBuffs.TryRemove(existingKey, out _);

        target.ActiveBuffs[magic.Id] = new ActiveBuff
        {
            MagicId = magic.Id,
            CasterId = -npc.UniqueId,
            Duration = type4Data.Duration,
            ExpireTicks = DateTime.UtcNow.AddSeconds(type4Data.Duration).Ticks,
            BuffType = buffType,
            SpecialAmount = type4Data.SpecialAmount > 0 ? type4Data.SpecialAmount : type4Data.ExpPct,
            BonusAc = type4Data.Ac,
            BonusAcPct = type4Data.AcPct,
            BonusAttack = type4Data.Attack,
            BonusMagicAttack = type4Data.MagicAttack,
            BonusMaxHp = type4Data.MaxHP,
            BonusMaxHpPct = type4Data.MaxHPPct,
            BonusMaxMp = type4Data.MaxMP,
            BonusMaxMpPct = type4Data.MaxMPPct,
            BonusHitRate = type4Data.HitRate,
            BonusAvoidRate = type4Data.AvoidRate,
            BonusStr = type4Data.Str,
            BonusSta = type4Data.Sta,
            BonusDex = type4Data.Dex,
            BonusIntel = type4Data.Intel,
            BonusCha = type4Data.Cha,
            BonusFireR = type4Data.FireR,
            BonusColdR = type4Data.ColdR,
            BonusLightningR = type4Data.LightningR,
            BonusMagicR = type4Data.MagicR,
            BonusPoisonR = type4Data.PoisonR,
            BonusDiseaseR = type4Data.DiseaseR,
            BonusSpeed = type4Data.Speed,
            BonusAttackSpeed = type4Data.AttackSpeed
        };

        target.RecalculateStatsWithBuffs(gameData);
        await userNotificationService.SendStatUpdateAsync(target);

        var effectPacket = CreateMagicProcessPacket(
            MagicEffecting,
            magic.Id,
            npc.UniqueId,
            target.CharacterId,
            type4Data.Duration);
        await sessionManager.Regions.SendToRegion(target, effectPacket, excludeSender: false);
    }

    private static int CalculateMeleeMagicDamage(NpcInstance npc, UserSession target, MagicType1Data type1)
    {
        var totalHit = npc.Attack1 > 0 ? npc.Attack1 : npc.Attack2;
        var baseDamage = totalHit * (100 + type1.Hit) / 100 + type1.AddDamage;
        var damage = baseDamage * 200 / (target.Stats.TotalAc + 240);
        var variance = Random.Shared.Next(0, Math.Max(1, damage / 5));
        return damage + variance;
    }

    private static int CalculateRangedMagicDamage(NpcInstance npc, UserSession target, MagicType2Data type2)
    {
        var totalHit = npc.Attack1 > 0 ? npc.Attack1 : npc.Attack2;
        var baseDamage = totalHit + type2.AddDamage;
        var damage = baseDamage * 200 / (target.Stats.TotalAc + 240);
        var variance = Random.Shared.Next(0, Math.Max(1, damage / 5));
        return damage + variance;
    }

    private static int CalculateOverTimeMagicDamage(NpcInstance npc, MagicType3Data type3)
    {
        return type3.FirstDamage > 0 ? type3.FirstDamage : (npc.Attack1 > 0 ? npc.Attack1 : npc.Attack2);
    }

    private async Task ExecuteHealEffectAsync(NpcInstance healer, NpcInstance target, MagicData magic)
    {
        var healAmount = gameData.MagicType3Table.TryGetValue(magic.Id, out var type3)
            ? type3.FirstDamage > 0 ? type3.FirstDamage : healer.MaxHp / 5
            : healer.MaxHp / 5;

        healAmount = Math.Max(1, healAmount);
        target.Hp = Math.Min(target.MaxHp, target.Hp + healAmount);

        var effectPacket = CreateMagicProcessPacket(MagicEffecting, magic.Id, healer.UniqueId, target.UniqueId);
        await sessionManager.Regions.BroadcastFromNpc(healer, effectPacket);
    }

    private static Packet CreateMagicProcessPacket(byte state, int magicId, int casterId, int targetId, short firstData = 0) =>
        MagicProcessPacketWriter.Create(
            (MagicProcessOpcode)state, magicId, casterId, targetId, [firstData]);

    private static void ResetCastingState(NpcInstance npc, NpcState nextState, long nowTicks)
    {
        npc.State = nextState;
        npc.ActiveSkillId = 0;
        npc.ActiveTargetId = 0;
        npc.HealTargetIsNpc = false;
        npc.StateChangeTicks = nowTicks;
    }
}
