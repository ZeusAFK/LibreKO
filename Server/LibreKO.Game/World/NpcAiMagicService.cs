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
    Task<bool> TryStartAttackCastAsync(NpcInstance npc, UserSession target, long nowTicks, bool instant = false);
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
    private const int ItemGrantedSkillIdBase = 400000;
    private const int ManaDrainCasterShare = 2;

    public async Task<bool> TryStartAttackCastAsync(NpcInstance npc, UserSession target, long nowTicks, bool instant = false)
    {
        var skillId = PickAttackSpell(npc);
        var magic = skillId == 0 ? null : gameData.GetMagic(skillId);
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

        if (instant)
        {
            await ExecuteMagicEffectAsync(npc, target, magic, nowTicks);
            return true;
        }

        npc.ActiveSkillId = skillId;
        npc.ActiveTargetId = target.CharacterId;
        npc.CastEndTicks = nowTicks + TimeSpan.FromMilliseconds(magic.CastTimeMs).Ticks;
        npc.State = NpcState.Casting;

        var castPacket = CreateMagicProcessPacket(MagicCasting, skillId, npc.UniqueId, target.CharacterId);
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

    private int PickAttackSpell(NpcInstance npc)
    {
        var picked = NpcSpellChoice.Pick(npc.Magic1, npc.Magic2, npc.Magic3, Random.Shared.Next(NpcSpellChoice.SpellRollRange));
        if (CastsAtPlayers(picked))
            return picked;

        return CastsAtPlayers(npc.Magic1) ? npc.Magic1 : 0;
    }

    private bool CastsAtPlayers(int skillId) =>
        skillId != 0
        && gameData.GetMagic(skillId) is { } magic
        && IsHostileMoral(magic.Moral)
        && magic.PrimaryType switch
        {
            MagicSkillType.OverTime => MagicTypeLookup.TryResolve(gameData.MagicType3Table, magic, skillId, out _),
            MagicSkillType.Buff => MagicTypeLookup.TryResolve(gameData.MagicType4Table, magic, skillId, out _),
            MagicSkillType.Melee => gameData.MagicType1Table.ContainsKey(skillId),
            MagicSkillType.Ranged => gameData.MagicType2Table.ContainsKey(skillId),
            _ => false,
        };

    private static bool IsHostileMoral(byte moral) =>
        (SkillMoral)moral is SkillMoral.Enemy or SkillMoral.AreaEnemy or SkillMoral.SelfArea;

    private static bool IsAreaMoral(byte moral) =>
        (SkillMoral)moral is SkillMoral.AreaEnemy or SkillMoral.SelfArea;

    private async Task ExecuteMagicEffectAsync(NpcInstance npc, UserSession target, MagicData magic, long nowTicks)
    {
        switch (magic.PrimaryType)
        {
            case MagicSkillType.Buff:
                foreach (var victim in SpellTargets(npc, target, magic))
                    await ApplyNpcDebuffAsync(npc, victim, magic);
                return;
            case MagicSkillType.OverTime when MagicTypeLookup.TryResolve(gameData.MagicType3Table, magic, magic.Id, out var type3):
                foreach (var victim in SpellTargets(npc, target, magic))
                    await StrikeOverTimeAsync(npc, victim, magic, type3, nowTicks);
                return;
            default:
                await StrikeWithSkillAsync(npc, target, magic, nowTicks);
                return;
        }
    }

    private List<UserSession> SpellTargets(NpcInstance npc, UserSession target, MagicData magic)
    {
        if (!IsAreaMoral(magic.Moral))
            return [target];

        var radius = AreaRadius(magic);
        var radiusSq = (float)radius * radius;
        var victims = new List<UserSession>();
        foreach (var player in sessionManager.Regions.GetNearbyUsersForNpc(npc))
        {
            if (player.Hp <= 0 || player.IsGM || player.IsBot || player.ZoneId != npc.ZoneId || player.IsInvisible || player.IsBlinking)
                continue;

            if (!NpcHostility.IsAttackableBy(npc, player))
                continue;

            var dx = player.X - target.X;
            var dz = player.Z - target.Z;
            if (radius > 0 && dx * dx + dz * dz > radiusSq)
                continue;

            victims.Add(player);
        }

        return victims;
    }

    private int AreaRadius(MagicData magic) => magic.PrimaryType switch
    {
        MagicSkillType.OverTime when MagicTypeLookup.TryResolve(gameData.MagicType3Table, magic, magic.Id, out var type3) => type3.Radius,
        MagicSkillType.Buff when MagicTypeLookup.TryResolve(gameData.MagicType4Table, magic, magic.Id, out var type4) => type4.Radius,
        _ => 0,
    };

    private async Task StrikeOverTimeAsync(NpcInstance npc, UserSession victim, MagicData magic, MagicType3Data type3, long nowTicks)
    {
        if (!await ApplyOverTimeSpellAsync(npc, victim, magic, type3))
            return;

        if (victim.Hp <= 0)
        {
            await KillAsync(npc, victim, magic.Id, nowTicks);
            return;
        }

        if (magic.SecondaryType == MagicSkillType.Buff)
            await ApplyNpcDebuffAsync(npc, victim, magic);
    }

    private async Task<bool> ApplyOverTimeSpellAsync(NpcInstance npc, UserSession target, MagicData magic, MagicType3Data type3)
    {
        if (target.BlockMagic && (type3.FirstDamage < 0 || type3.TimeDamage < 0))
            return false;

        var directType = (MagicDirectType)type3.DirectType;
        var amount = type3.FirstDamage < 0 && ScalesWithMagicAttack(directType, magic.Id)
            ? -MagicCombatHelper.GetMagicDamage(npc, target, -type3.FirstDamage, type3.Attribute)
            : type3.FirstDamage;

        await SendEffectAsync(npc, target, magic.Id);

        if (type3.Duration == 0)
            await ApplyInstantAsync(npc, target, type3, directType, amount);
        else
            await ApplyDurationalAsync(npc, target, magic.Id, type3, amount);

        logger.LogDebug(
            "NPC spell landed: npc={NpcId}/{UniqueId} target={TargetId} magicId={MagicId} directType={DirectType} amount={Amount} hpAfter={HpAfter}",
            npc.NpcId, npc.UniqueId, target.CharacterId, magic.Id, directType, amount, target.Hp);
        return true;
    }

    private static bool ScalesWithMagicAttack(MagicDirectType directType, int skillId) =>
        directType is MagicDirectType.Health or MagicDirectType.DamageAbsorb
        && skillId < ItemGrantedSkillIdBase;

    private async Task ApplyInstantAsync(
        NpcInstance npc, UserSession target, MagicType3Data type3, MagicDirectType directType, int amount)
    {
        switch (directType)
        {
            case MagicDirectType.Health:
            case MagicDirectType.RawDamage:
            case MagicDirectType.FixedHealth:
                await ChangeHpAsync(npc, target, amount);
                break;
            case MagicDirectType.Mana:
                await ChangeMpAsync(target, type3.FirstDamage);
                break;
            case MagicDirectType.ManaShell:
                await ChangeMpAsync(target, amount);
                break;
            case MagicDirectType.HealthPercent:
                await ChangeHpAsync(npc, target, OverTimeEffects.PercentOfHealth(target.Hp, target.MaxHp, type3.FirstDamage));
                break;
            case MagicDirectType.DamageAbsorb:
                await DrainHpAsync(npc, target, amount);
                break;
            case MagicDirectType.PercentDrain:
                await DrainHpAsync(npc, target, OverTimeEffects.PercentOfHealth(target.Hp, target.MaxHp, type3.FirstDamage));
                break;
            case MagicDirectType.ManaDrain:
                await ChangeMpAsync(target, type3.FirstDamage);
                ChangeNpcHp(npc, -type3.FirstDamage / ManaDrainCasterShare);
                break;
        }
    }

    private async Task ApplyDurationalAsync(NpcInstance npc, UserSession target, int skillId, MagicType3Data type3, int amount)
    {
        if (amount != 0)
            await ChangeHpAsync(npc, target, amount);

        if (target.Hp <= 0)
            return;

        var total = type3.TimeDamage < 0 && (MagicAttribute)type3.Attribute != MagicAttribute.Magic
            ? -MagicCombatHelper.GetMagicDamage(npc, target, -type3.TimeDamage, type3.Attribute)
            : type3.TimeDamage;
        if (total < 0 && target.BlockMagic)
            return;

        OverTimeEffects.Schedule(target.ActiveOverTimeEffects, skillId, -npc.UniqueId, total, type3.Duration);
        if (type3.TimeDamage >= 0 || !MarksOverTimeDamage(type3, skillId)
            || !target.ActiveOverTimeEffects.TryGetValue(skillId, out var applied) || applied.TickAmount >= 0)
            return;

        applied.PartyStatusCode = OverTimeEffects.PartyStatusOf(type3.Attribute);
        await combatNotificationService.SendPartyStatusUpdateAsync(target, applied.PartyStatusCode, applied: true);
    }

    private static bool MarksOverTimeDamage(MagicType3Data type3, int skillId) =>
        !((MagicDirectType)type3.DirectType is MagicDirectType.None or MagicDirectType.Health
          && skillId < ItemGrantedSkillIdBase
          && (MagicAttribute)type3.Attribute is MagicAttribute.Lightning or MagicAttribute.Cold);

    private async Task ChangeHpAsync(NpcInstance npc, UserSession target, int delta)
    {
        if (delta < 0)
        {
            var damage = GmMode.Taken(target, Math.Min(-delta, CombatUtils.MaxDamage));
            if (damage <= 0)
                return;

            target.Hp = (short)Math.Max(0, target.Hp - damage);
            target.MarkCombat();
            await combatNotificationService.SendHpChangeAsync(target, npc.UniqueId);
        }
        else if (delta > 0 && target.Hp < target.MaxHp)
        {
            target.Hp = (short)Math.Min(target.MaxHp, target.Hp + delta);
            await combatNotificationService.SendHpChangeAsync(target);
        }
    }

    private async Task ChangeMpAsync(UserSession target, int delta)
    {
        var mp = (short)Math.Clamp(target.Mp + delta, 0, target.MaxMp);
        if (mp == target.Mp)
            return;

        target.Mp = mp;
        if (delta < 0)
            target.MarkCombat();
        await combatNotificationService.SendMspChangeAsync(target);
    }

    private async Task DrainHpAsync(NpcInstance npc, UserSession target, int amount)
    {
        await ChangeHpAsync(npc, target, amount);
        ChangeNpcHp(npc, -amount);
    }

    private static void ChangeNpcHp(NpcInstance npc, int delta)
    {
        if (delta != 0 && npc.IsAlive)
            npc.Hp = Math.Clamp(npc.Hp + delta, 1, npc.MaxHp);
    }

    private async Task SendEffectAsync(NpcInstance npc, UserSession target, int magicId)
    {
        var effectPacket = CreateMagicProcessPacket(MagicEffecting, magicId, npc.UniqueId, target.CharacterId);
        await sessionManager.Regions.SendToRegion(target, effectPacket, excludeSender: false);
    }

    private async Task KillAsync(NpcInstance npc, UserSession victim, int skillId, long nowTicks)
    {
        victim.ActiveOverTimeEffects.TryRemove(skillId, out _);
        await npcAiDeathService.HandlePlayerKilledByNpcAsync(victim, npc);
        if (victim.CharacterId == npc.TargetUserId)
            npcAiTargetingService.LoseTarget(npc, nowTicks);
    }

    private async Task StrikeWithSkillAsync(NpcInstance npc, UserSession target, MagicData magic, long nowTicks)
    {
        var damage = magic.PrimaryType switch
        {
            MagicSkillType.Melee when gameData.MagicType1Table.TryGetValue(magic.Id, out var melee)
                => CalculateMeleeMagicDamage(npc, target, melee),
            MagicSkillType.Ranged when gameData.MagicType2Table.TryGetValue(magic.Id, out var ranged)
                => CalculateRangedMagicDamage(npc, target, ranged),
            _ => 0
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

        damage = GmMode.Taken(target, Math.Clamp(damage, 0, CombatUtils.MaxDamage));
        if (damage > 0)
        {
            target.Hp = (short)Math.Max(0, target.Hp - damage);
            target.MarkCombat();
        }

        await SendEffectAsync(npc, target, magic.Id);

        if (damage > 0)
            await combatNotificationService.SendHpChangeAsync(target, npc.UniqueId);

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
        target.ReplaceBuff(existingKey, magic.Id, new ActiveBuff
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
        });

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
        var totalHit = npc.TotalHit;
        var baseDamage = totalHit * (100 + type1.Hit) / 100 + type1.AddDamage;
        var damage = baseDamage * 200 / (target.Stats.TotalAc + 240);
        var variance = Random.Shared.Next(0, Math.Max(1, damage / 5));
        return damage + variance;
    }

    private static int CalculateRangedMagicDamage(NpcInstance npc, UserSession target, MagicType2Data type2)
    {
        var totalHit = npc.TotalHit;
        var baseDamage = totalHit + type2.AddDamage;
        var damage = baseDamage * 200 / (target.Stats.TotalAc + 240);
        var variance = Random.Shared.Next(0, Math.Max(1, damage / 5));
        return damage + variance;
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
