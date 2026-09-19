using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMagicStatusEffectService
{
    Task ExecuteAsync(
        UserSession caster, MagicData magic, MagicSkillType skillType, int skillId, int targetId,
        int[] data);
    Task CancelAsync(UserSession session, int skillId);
}

public class MagicStatusEffectService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMagicItemUsageService magicItemUsageService,
    ICombatLifecycleService combatLifecycleService,
    ICombatNotificationService combatNotificationService,
    IUserNotificationService userNotificationService,
    IPlayerProgressionService playerProgressionService,
    IStealthService stealthService,
    ILogger<MagicStatusEffectService> logger) : IMagicStatusEffectService
{
    private const int SkillSucceeded = 1;

    public Task ExecuteAsync(
        UserSession caster, MagicData magic, MagicSkillType skillType, int skillId, int targetId,
        int[] data) =>
        skillType switch
        {
            MagicSkillType.Buff => ExecuteBuffAsync(caster, magic, skillId, targetId, data),
            MagicSkillType.Special => ExecuteSpecialAsync(caster, magic, skillId, targetId),
            MagicSkillType.Transform => ExecuteTransformAsync(caster, magic, skillId, targetId),
            MagicSkillType.Stealth => ExecuteStealthAsync(caster, magic, skillId, targetId, data),
            _ => Task.CompletedTask
        };

    public async Task CancelAsync(UserSession session, int skillId)
    {
        if (!session.ActiveBuffs.TryRemove(skillId, out var removedBuff))
            return;

        var magicRow = gameDataService.GetMagic(skillId);
        if (magicRow?.PrimaryType == MagicSkillType.Transform && session.IsTransformed)
            await EndTransformationAsync(session, skillId);

        session.RebuildSpecialStates(gameDataService);
        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendStatUpdateAsync(session);

        if (magicRow?.PrimaryType == MagicSkillType.Stealth
            && MagicTypeLookup.TryResolve(
                gameDataService.MagicType9Table, magicRow, skillId, out var expiredType9))
        {
            await stealthService.EndAsync(session, (MagicStealthType)expiredType9.StateChange);
            return;
        }

        var magic = gameDataService.GetMagic(skillId);
        if (magic?.PrimaryType == MagicSkillType.Buff && removedBuff != null && removedBuff.BuffType != BuffType.None)
        {
            await session.Client.SendPacket(
                MagicProcessPacketWriter.CreateDurationExpired((byte)removedBuff.BuffType));

            // Drop the party-panel status icon on natural debuff expiry.
            var statusType = GetPartyStatusCode(removedBuff.BuffType);
            if (statusType > 0)
                await combatNotificationService.SendPartyStatusUpdateAsync(session, statusType, applied: false);
            return;
        }

        await sessionManager.Regions.SendToRegion(
            session,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.DurationExpired,
                skillId,
                (short)session.CharacterId,
                (short)session.CharacterId),
            excludeSender: false);
    }

    private const int DisguiseScrollItem = 381001000;

    private bool HasTransformationItems(UserSession caster, MagicData magic, MagicType6Data type6Data)
    {
        if ((TransformationUse)type6Data.UserSkillUse == TransformationUse.Monster)
        {
            return (magicItemUsageService.CanUseItem(caster, magic.BeforeAction)
                    && magicItemUsageService.CanUseItem(caster, magic.UseItem))
                || magicItemUsageService.CanUseItem(caster, DisguiseScrollItem);
        }

        return magic.UseItem == 0 || magicItemUsageService.CanUseItem(caster, magic.UseItem);
    }

    private static bool IsTransformationClassAllowed(short classId, short allowedClasses)
    {
        if (allowedClasses == 0)
            return true;

        var digit = ClassIdHelper.IsWarrior(classId) ? allowedClasses / 1000
            : ClassIdHelper.IsRogue(classId) ? allowedClasses % 1000 / 100
            : ClassIdHelper.IsMage(classId) ? allowedClasses % 100 / 10
            : allowedClasses % 10;

        return digit == 1;
    }

    private async Task EndTransformationAsync(UserSession session, int skillId)
    {
        session.TransformId = 0;

        await sessionManager.Regions.SendToRegion(
            session,
            MovementPacketWriter.Transformation(
                session.CharacterId, (byte)StateChangeType.Transformation, NotTransformed),
            excludeSender: false);

        await session.Client.SendPacket(MagicProcessPacketWriter.CreateCancelTransformation());
    }

    private const int NotTransformed = 0;

    private async Task ExecuteBuffAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, skillId, out var type4Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        if (magic.UseItem != 0)
        {
            if ((magic.ItemGroup == PotionItemGroup && !caster.CanUsePotions)
                || !magicItemUsageService.CanUseItem(caster, magic.UseItem))
            {
                logger.LogWarning(
                    "Skill {SkillId} refused for {Name}: item {ItemId} is {Reason} (class {Class}, level {Level})",
                    skillId, caster.Name, magic.UseItem,
                    magicItemUsageService.CheckItem(caster, magic.UseItem), caster.Class, caster.Level);
                await SendMagicFailAsync(caster, skillId);
                return;
            }
        }

        var target = ResolveBuffTarget(caster, magic, targetId);
        if (target == null)
            return;

        var buffType = (BuffType)type4Data.BuffType;
        var isDebuff = !MagicBuffClassifier.IsBuff(type4Data);

        if (isDebuff && target.CharacterId != caster.CharacterId)
        {
            if (target.BlockCurses)
            {
                await SendMagicFailAsync(caster, skillId);
                return;
            }

            if (target.ReflectCurses && Random.Shared.Next(100) < 25)
                target = caster;

            if (magic.SuccessRate > 0 && magic.SuccessRate < 100 && Random.Shared.Next(100) >= magic.SuccessRate)
            {
                await SendMagicFailAsync(caster, skillId);
                return;
            }
        }

        var existingKey = buffType == BuffType.None
            ? 0
            : target.ActiveBuffs
                .FirstOrDefault(kvp => kvp.Value.BuffType == buffType && !kvp.Value.IsExpired)
                .Key;
        if (existingKey > 0)
        {
            if (!isDebuff)
            {
                await SendMagicFailAsync(caster, skillId);
                return;
            }

            target.ActiveBuffs.TryRemove(existingKey, out _);
        }

        logger.LogDebug("Buff {SkillId} applied to {Name} type={BuffType} duration={Duration}s",
            skillId, target.Name, buffType, type4Data.Duration);

        target.ActiveBuffs[skillId] = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = caster.CharacterId,
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

        target.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendStatUpdateAsync(target);

        // If this buff is a debuff that maps to a party-panel status icon, notify the
        // target's party so the icon lights up.
        var statusType = GetPartyStatusCode(buffType);
        if (statusType > 0 && isDebuff)
            await combatNotificationService.SendPartyStatusUpdateAsync(target, statusType, applied: true);

        if (magic.UseItem != 0 && !await magicItemUsageService.TryConsumeItemAsync(caster, magic.UseItem))
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        // Data layout: [data[0], bResult, data[2], sDuration, data[4], bSpeed, data[6]]
        await sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                (short)target.CharacterId,
                [data[0], 1, data[2], type4Data.Duration, data[4], type4Data.Speed, data[6]]),
            excludeSender: false);
    }

    private UserSession? ResolveBuffTarget(UserSession caster, MagicData magic, int targetId)
    {
        if ((SkillMoral)magic.Moral == SkillMoral.Self || targetId == NoTarget || targetId == caster.CharacterId)
            return caster;

        return sessionManager.GetByCharacterId(targetId);
    }

    private const byte PotionItemGroup = 9;
    private const int NoTarget = -1;

    private async Task ExecuteSpecialAsync(UserSession caster, MagicData magic, int skillId, int targetId)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType5Table, magic, skillId, out var type5Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        var special = (SpecialMagicType)type5Data.Type;

        // Sub-types 4 (self-resurrect) and 6 (life crystal) always target the caster.
        UserSession target;
        if (special is SpecialMagicType.ResurrectionSelf or SpecialMagicType.LifeCrystal)
            target = caster;
        else
        {
            var resolved = sessionManager.GetByCharacterId(targetId);
            if (resolved == null)
            {
                await SendMagicFailAsync(caster, skillId);
                return;
            }
            target = resolved;
        }

        switch (special)
        {
            case SpecialMagicType.RemoveDot: // clear harmful DOTs (negative tick = damage)
            {
                var harmful = target.ActiveOverTimeEffects
                    .Where(kv => kv.Value.TickAmount < 0)
                    .ToList();
                if (harmful.Count == 0) break;

                // Capture distinct status codes BEFORE removing so we can clear each
                // icon flavor that was actually present (poison + disease + generic).
                var clearedCodes = harmful
                    .Select(kv => kv.Value.PartyStatusCode)
                    .Where(c => c > 0)
                    .Distinct()
                    .ToList();

                foreach (var (id, _) in harmful)
                    target.ActiveOverTimeEffects.TryRemove(id, out _);

                await target.Client.SendPacket(MagicProcessPacketWriter.CreateDurationExpired(200));

                // Drop one status-clear per distinct flavor that was active.
                foreach (var code in clearedCodes)
                    await combatNotificationService.SendPartyStatusUpdateAsync(target, code, applied: false);
                break;
            }

            case SpecialMagicType.RemoveBuff: // clear all type-4 debuffs cast by others
            case SpecialMagicType.RemoveBless: // same shape (single buff dispel)
            {
                if (await ClearDebuffsAsync(target))
                {
                    target.RebuildSpecialStates(gameDataService);
                    target.RecalculateStatsWithBuffs(gameDataService);
                    await userNotificationService.SendStatUpdateAsync(target);
                }
                break;
            }

            case SpecialMagicType.Resurrection:
            case SpecialMagicType.ResurrectionSelf:
            case SpecialMagicType.LifeCrystal:
            {
                if (target.Hp > 0)
                {
                    await SendMagicFailAsync(caster, skillId);
                    return;
                }

                if (special == SpecialMagicType.Resurrection
                    && magic.UseItem != 0
                    && type5Data.NeedStone > 0
                    && !await magicItemUsageService.TryConsumeItemAsync(
                        target, magic.UseItem, type5Data.NeedStone))
                {
                    await SendMagicFailAsync(caster, skillId);
                    return;
                }

                target.Hp = target.MaxHp;
                target.Mp = 0;

                var recovered = target.DeathExpLoss > 0 && type5Data.ExpRecover > 0
                    ? target.DeathExpLoss * type5Data.ExpRecover / 100
                    : 0;
                target.DeathExpLoss = 0;
                if (recovered > 0)
                    await playerProgressionService.ChangeExperienceAsync(target, recovered);

                await combatLifecycleService.SendHpChangeAsync(target);
                await combatNotificationService.SendMspChangeAsync(target);

                // Broadcast resurrection to region so other players see the player stand up.
                await sessionManager.Regions.SendToRegion(
                    target,
                    MagicProcessPacketWriter.Create(
                        MagicProcessOpcode.Effecting,
                        skillId,
                        (short)caster.CharacterId,
                        (short)target.CharacterId,
                        [0, 1]),
                    excludeSender: false);
                return; // Already broadcast above; skip the trailing region broadcast.
            }
        }

        // Sets sData[1] = 1 (success) and preserves original client data
        await sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                targetId,
                [0, 1]),
            excludeSender: false);
    }

    private async Task<bool> ClearDebuffsAsync(UserSession target)
    {
        // A debuff is any type-4 buff cast by someone other than the target itself.
        var debuffIds = target.ActiveBuffs
            .Where(kv => kv.Value.CasterId != target.CharacterId && kv.Value.BuffType != BuffType.None)
            .Select(kv => kv.Key)
            .ToList();
        if (debuffIds.Count == 0)
            return false;

        foreach (var id in debuffIds)
        {
            if (target.ActiveBuffs.TryRemove(id, out var removed) && removed.BuffType != BuffType.None)
            {
                await target.Client.SendPacket(
                    MagicProcessPacketWriter.CreateDurationExpired((byte)removed.BuffType));

                // Drop the party-panel status icon for cured debuffs.
                var statusType = GetPartyStatusCode(removed.BuffType);
                if (statusType > 0)
                    await combatNotificationService.SendPartyStatusUpdateAsync(target, statusType, applied: false);
            }
        }
        return true;
    }

    private static byte GetPartyStatusCode(BuffType buffType) => (byte)(buffType switch
    {
        BuffType.Blind or BuffType.DisableTargeting or BuffType.Unsight => PartyStatusIcon.Blind,
        _ => PartyStatusIcon.None,
    });

    private async Task ExecuteTransformAsync(UserSession caster, MagicData magic, int skillId, int targetId)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType6Table, magic, skillId, out var type6Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        var target = (SkillMoral)magic.Moral == SkillMoral.Self
            ? caster
            : sessionManager.GetByCharacterId(targetId) ?? caster;

        switch ((TransformationUse)type6Data.UserSkillUse)
        {
            case TransformationUse.GuardTower:
                await SendMagicFailAsync(caster, skillId);
                return;
            case TransformationUse.MovingTower:
                if (caster.ZoneId != ZoneDelos)
                {
                    await SendMagicFailAsync(caster, skillId);
                    return;
                }
                break;
        }

        if (!HasTransformationItems(caster, magic, type6Data)
            || !IsTransformationClassAllowed(caster.Class, type6Data.Class))
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        if (type6Data.Nation != (byte)EntityNation.All && (byte)target.Nation != type6Data.Nation)
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        if (target.IsTransformed)
        {
            logger.LogWarning(
                "Transformation {SkillId} refused for {Name}: already transformed as {TransformId}",
                skillId, caster.Name, target.TransformId);
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        if (type6Data.SkillSuccessRate > 0 && type6Data.SkillSuccessRate < 100
            && Random.Shared.Next(100) >= type6Data.SkillSuccessRate)
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        target.TransformId = type6Data.TransformId;
        target.ActiveBuffs[skillId] = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = caster.CharacterId,
            Duration = type6Data.Duration,
            ExpireTicks = DateTime.UtcNow.AddSeconds(type6Data.Duration).Ticks
        };

        var statePkt = MovementPacketWriter.Transformation(
            target.CharacterId, (byte)StateChangeType.Transformation, skillId);
        await sessionManager.Regions.SendToRegion(target, statePkt, excludeSender: false);

        int[] effectingData = [0, 1, 0, type6Data.Duration];
        await sessionManager.Regions.SendToRegion(
            target,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                caster.CharacterId,
                target.CharacterId,
                effectingData),
            excludeSender: false);

        if (magic.UseItem != 0)
            await magicItemUsageService.TryConsumeItemAsync(caster, magic.UseItem);
    }

    private const byte ZoneDelos = (byte)ZoneId.Delos;

    private const byte ZoneForgottenTemple = (byte)ZoneId.ForgottenTemple;
    private const byte ZoneDungeonDefence = (byte)ZoneId.DungeonDefence;

    private async Task ExecuteStealthAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType9Table, magic, skillId, out var type9Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        var target = (SkillMoral)magic.Moral == SkillMoral.Self
            ? caster
            : sessionManager.GetByCharacterId(targetId) ?? caster;

        var stealthType = (MagicStealthType)type9Data.StateChange;
        var applied = stealthType switch
        {
            MagicStealthType.DispelOnMove or MagicStealthType.DispelOnAttack =>
                await HideAsync(caster, target, stealthType, skillId, type9Data),
            MagicStealthType.SeeInvisible or MagicStealthType.SeeInvisibleParty =>
                await GrantSightAsync(caster, stealthType, skillId, type9Data, data),
            _ => UnhandledStealth(caster, stealthType, skillId),
        };

        if (!applied)
        {
            await SendMagicFailAsync(caster, skillId);
            return;
        }

        if (stealthType is MagicStealthType.DispelOnMove or MagicStealthType.DispelOnAttack)
            await AnnounceStealthAsync(caster, target, skillId, type9Data, data);
    }

    private async Task<bool> HideAsync(
        UserSession caster, UserSession target, MagicStealthType stealthType, int skillId,
        MagicType9Data type9Data)
    {
        if (caster.ZoneId == ZoneForgottenTemple || caster.ZoneId == ZoneDungeonDefence)
            return false;

        if (target.StealthProhibited || target.IsInvisible)
            return false;

        await stealthService.HideAsync(target, (InvisibilityType)stealthType);
        AddStealthBuff(target, caster, skillId, type9Data);
        return true;
    }

    private async Task<bool> GrantSightAsync(
        UserSession caster, MagicStealthType stealthType, int skillId, MagicType9Data type9Data,
        int[] data)
    {
        var party = stealthType == MagicStealthType.SeeInvisibleParty && caster.IsInParty
            ? sessionManager.Parties.GetParty(caster.PartyIndex)
            : null;

        if (party == null)
        {
            if (HoldsStealthState(caster, stealthType))
                return false;

            await stealthService.GrantSightAsync(caster, type9Data.Radius);
            AddStealthBuff(caster, caster, skillId, type9Data);
            await AnnounceStealthAsync(caster, caster, skillId, type9Data, data);
            return true;
        }

        var granted = false;
        foreach (var memberId in party.MemberIds)
        {
            if (memberId < 0)
                continue;

            var member = sessionManager.GetByCharacterId(memberId);
            if (member == null || HoldsStealthState(member, stealthType))
                continue;

            await stealthService.GrantSightAsync(member, type9Data.Radius);
            AddStealthBuff(member, caster, skillId, type9Data);
            await AnnounceStealthAsync(caster, member, skillId, type9Data, data);
            granted = true;
        }

        return granted;
    }

    private bool UnhandledStealth(UserSession caster, MagicStealthType stealthType, int skillId)
    {
        logger.LogDebug(
            "Unhandled stealth state {StealthType} on skill {SkillId} cast by {Name}",
            stealthType, skillId, caster.Name);
        return false;
    }

    private static void AddStealthBuff(
        UserSession target, UserSession caster, int skillId, MagicType9Data type9Data)
    {
        if (type9Data.Duration <= 0)
            return;

        target.ActiveBuffs[skillId] = new ActiveBuff
        {
            MagicId = skillId,
            CasterId = caster.CharacterId,
            Duration = type9Data.Duration,
            ExpireTicks = DateTime.UtcNow.AddSeconds(type9Data.Duration).Ticks
        };
    }

    private bool HoldsStealthState(UserSession session, MagicStealthType stealthType) =>
        session.ActiveBuffs.Keys.Any(activeId =>
            gameDataService.GetMagic(activeId) is { PrimaryType: MagicSkillType.Stealth } active
            && MagicTypeLookup.TryResolve(gameDataService.MagicType9Table, active, activeId, out var active9)
            && (MagicStealthType)active9.StateChange == stealthType);

    private Task AnnounceStealthAsync(
        UserSession caster, UserSession target, int skillId, MagicType9Data type9Data, int[] data) =>
        sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                (short)target.CharacterId,
                [data[0], SkillSucceeded, data[2], type9Data.Duration, data[4], data[5], data[6]]),
            excludeSender: false);

    private static Task SendMagicFailAsync(UserSession session, int skillId) =>
        session.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, session.CharacterId));
}
