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
    Task<bool> ExecuteAsync(
        UserSession caster, MagicData magic, MagicSkillType skillType, int skillId, int targetId,
        int[] data, bool isPrimary = true);
    Task CancelAsync(UserSession session, int skillId);
    Task<bool> TransformAsync(UserSession target, int skillId);
    event Func<UserSession, int, Task>? TransformationEnded;
}

public class MagicStatusEffectService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMagicItemUsageService magicItemUsageService,
    ICombatNotificationService combatNotificationService,
    IUserNotificationService userNotificationService,
    IStealthService stealthService,
    INpcSummonService npcSummonService,
    INpcLifecycleService npcLifecycleService,
    IPetService petService,
    IResurrectionOfferService resurrectionOfferService,
    IControlStateService controlStateService,
    ILogger<MagicStatusEffectService> logger) : IMagicStatusEffectService
{
    private const int SkillSucceeded = 1;
    private const int PetEffectNoDuration = -1;
    private const byte DamageOverTimeCured = 200;

    public event Func<UserSession, int, Task>? TransformationEnded;

    public Task<bool> ExecuteAsync(
        UserSession caster, MagicData magic, MagicSkillType skillType, int skillId, int targetId,
        int[] data, bool isPrimary = true) =>
        skillType switch
        {
            MagicSkillType.Buff => ExecuteBuffAsync(caster, magic, skillId, targetId, data, isPrimary),
            MagicSkillType.Special => ExecuteSpecialAsync(caster, magic, skillId, targetId),
            MagicSkillType.Transform => ExecuteTransformAsync(caster, magic, skillId, targetId),
            MagicSkillType.Stealth => ExecuteStealthAsync(caster, magic, skillId, targetId, data),
            _ => Task.FromResult(false)
        };

    public async Task CancelAsync(UserSession session, int skillId)
    {
        if (session.Pet is { IsSummoned: true } && SummonsPet(skillId))
        {
            await petService.DismissAsync(session);
            return;
        }

        if (!session.RemoveBuff(skillId, out var removedBuff))
            return;

        await SyncSizeStateAsync(session);
        await SyncDevilFormAsync(session);
        var magicRow = gameDataService.GetMagic(skillId);
        if (magicRow?.PrimaryType == MagicSkillType.Transform && session.IsTransformed)
            await EndTransformationAsync(session, skillId);

        session.RebuildSpecialStates(gameDataService);
        session.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendStatUpdateAsync(session);

        if (magicRow?.HasType(MagicSkillType.Stealth) == true
            && MagicTypeLookup.TryResolve(
                gameDataService.MagicType9Table, magicRow, skillId, out var expiredType9))
        {
            if ((MagicStealthType)expiredType9.StateChange == MagicStealthType.GuardSummon)
                await DismissGuardAsync(session);
            await stealthService.EndAsync(session, (MagicStealthType)expiredType9.StateChange);
            if (magicRow.PrimaryType == MagicSkillType.Stealth)
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
                    && magicItemUsageService.CanUseSkillItems(caster, magic))
                || magicItemUsageService.CanUseItem(caster, DisguiseScrollItem);
        }

        return magicItemUsageService.CanUseSkillItems(caster, magic);
    }

    private Task<bool> SpendTransformationItemsAsync(UserSession caster, MagicData magic, MagicType6Data type6Data) =>
        (TransformationUse)type6Data.UserSkillUse == TransformationUse.Monster
        && !(magicItemUsageService.CanUseItem(caster, magic.BeforeAction) && magicItemUsageService.CanUseSkillItems(caster, magic))
            ? magicItemUsageService.TryConsumeItemAsync(caster, DisguiseScrollItem)
            : magicItemUsageService.TryConsumeSkillItemAsync(caster, magic);

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
        session.TransformSkillId = 0;

        await sessionManager.Regions.SendToRegion(
            session,
            MovementPacketWriter.Transformation(
                session.CharacterId, (byte)StateChangeType.Transformation, NotTransformed),
            excludeSender: false);

        await session.Client.SendPacket(MagicProcessPacketWriter.CreateCancelTransformation());

        if (TransformationEnded is { } ended)
            foreach (var handler in ended.GetInvocationList().Cast<Func<UserSession, int, Task>>())
                await handler(session, skillId);
    }

    private const int NotTransformed = 0;

    private enum BuffOutcome
    {
        Applied,
        Refused,
        Skipped,
    }

    private const int AreaTarget = -1;
    private const int ReflectCurseChance = 25;
    private const float NpcDebuffReachSlack = 3f;
    private const float PlayerReachSlack = 3f;

    private async Task<bool> ExecuteBuffAsync(
        UserSession caster, MagicData magic, int skillId, int targetId, int[] data, bool isPrimary)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, skillId, out var type4Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (isPrimary && magic.UseItem != 0)
        {
            if ((magic.ItemGroup == PotionItemGroup && !caster.CanUsePotions)
                || !magicItemUsageService.CanUseSkillItems(caster, magic))
            {
                logger.LogWarning(
                    "Skill {SkillId} refused for {Name}: item {ItemId} is {Reason} (class {Class}, level {Level})",
                    skillId, caster.Name, magic.ConsumedItem,
                    magicItemUsageService.CheckItem(caster, magic.ConsumedItem), caster.Class, caster.Level);
                await SendMagicFailAsync(caster, skillId);
                return false;
            }
        }

        var isDebuff = !MagicBuffClassifier.IsBuff(type4Data);

        if (targetId == AreaTarget && (SkillMoral)magic.Moral != SkillMoral.Self)
        {
            return await ExecuteAreaBuffAsync(caster, magic, skillId, type4Data, isDebuff, data, isPrimary);
        }

        if (isDebuff
            && sessionManager.GetByCharacterId(targetId) == null
            && sessionManager.Regions.GetNpc(targetId) is { } npcTarget)
        {
            return await ExecuteNpcDebuffAsync(caster, magic, skillId, npcTarget, type4Data, data, isPrimary);
        }

        var target = ResolveBuffTarget(caster, magic, targetId);
        if (target == null)
            return false;

        var (outcome, affected) = await ApplyBuffToPlayerAsync(caster, target, magic, skillId, type4Data, isDebuff, isPrimary);
        if (outcome != BuffOutcome.Applied)
        {
            if (outcome == BuffOutcome.Refused && isPrimary)
                await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (isPrimary && !await magicItemUsageService.TryConsumeSkillItemAsync(caster, magic))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        await AnnounceBuffAsync(caster, affected.CharacterId, skillId, type4Data, data);
        return true;
    }

    private async Task<bool> ExecuteAreaBuffAsync(
        UserSession caster, MagicData magic, int skillId, MagicType4Data type4Data, bool isDebuff,
        int[] data, bool isPrimary)
    {
        var moral = (SkillMoral)magic.Moral;
        var centreX = AreaCoordinate(data[0], caster.X);
        var centreZ = AreaCoordinate(data[2], caster.Z);
        var applied = false;

        if (isPrimary && !await magicItemUsageService.TryConsumeSkillItemAsync(caster, magic))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        foreach (var player in AreaPlayers(caster, moral, type4Data.Radius, centreX, centreZ).ToList())
        {
            var (outcome, affected) = await ApplyBuffToPlayerAsync(caster, player, magic, skillId, type4Data, isDebuff, isPrimary);
            if (outcome != BuffOutcome.Applied)
                continue;

            applied = true;
            await AnnounceBuffAsync(caster, affected.CharacterId, skillId, type4Data, data);
        }

        if (isDebuff && IsHostileMoral(moral))
        {
            foreach (var npc in AreaNpcs(caster, type4Data.Radius + NpcDebuffReachSlack, centreX, centreZ).ToList())
            {
                if (!TryDebuffNpc(caster, magic, npc, skillId, type4Data))
                    continue;

                applied = true;
                await AnnounceBuffAsync(caster, npc.UniqueId, skillId, type4Data, data);
            }
        }

        if (!isPrimary)
            return applied;

        if (!applied || moral >= SkillMoral.All)
        {
            await sessionManager.Regions.SendToRegion(
                caster,
                MagicProcessPacketWriter.Create(
                    MagicProcessOpcode.Effecting, skillId, (short)caster.CharacterId, AreaTarget, data),
                excludeSender: false);
        }
        return true;
    }

    private async Task<bool> ExecuteNpcDebuffAsync(
        UserSession caster, MagicData magic, int skillId, NpcInstance npc, MagicType4Data type4Data,
        int[] data, bool isPrimary)
    {
        if (!IsHostileMoral((SkillMoral)magic.Moral)
            || !NpcDebuffs.Affects((BuffType)type4Data.BuffType)
            || !TryDebuffNpc(caster, magic, npc, skillId, type4Data))
        {
            if (isPrimary)
                await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (isPrimary && !await magicItemUsageService.TryConsumeSkillItemAsync(caster, magic))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        await AnnounceBuffAsync(caster, npc.UniqueId, skillId, type4Data, data);
        await controlStateService.SyncAsync(npc);
        return true;
    }

    private static bool TryDebuffNpc(
        UserSession caster, MagicData magic, NpcInstance npc, int skillId, MagicType4Data type4Data)
    {
        if (!IsDebuffableNpc(npc, caster) || !LandsDebuff(magic))
            return false;

        return npc.Debuffs.TryApply(skillId, type4Data, DateTime.UtcNow.Ticks);
    }

    private async Task<(BuffOutcome Outcome, UserSession Target)> ApplyBuffToPlayerAsync(
        UserSession caster, UserSession target, MagicData magic, int skillId, MagicType4Data type4Data,
        bool isDebuff, bool isPrimary)
    {
        var buffType = (BuffType)type4Data.BuffType;

        if (isDebuff)
        {
            if (target.CharacterId == caster.CharacterId)
            {
                if (IsHostileMoral((SkillMoral)magic.Moral))
                    return (BuffOutcome.Skipped, target);
            }
            else
            {
                if (target.BlockCurses)
                    return (BuffOutcome.Refused, target);

                if (target.ReflectCurses && Random.Shared.Next(100) < ReflectCurseChance)
                    target = caster;

                if (!LandsOnPlayer(caster, target, magic, buffType, isPrimary))
                    return (BuffOutcome.Refused, target);
            }
        }

        if ((buffType == BuffType.Speed || type4Data.Speed > 100)
            && (target.ActiveBuffs.Values.Any(b => b.BuffType == BuffType.FragmentOfManes && !b.IsExpired)
                || caster.ActiveBuffs.Values.Any(b => b.BuffType == BuffType.FragmentOfManes && !b.IsExpired)))
            return (BuffOutcome.Refused, target);

        if (buffType == BuffType.FragmentOfManes)
        {
            var activeSpeedBuffs = target.ActiveBuffs
                .Where(kvp => (kvp.Value.BuffType == BuffType.Speed || kvp.Value.BuffType == BuffType.Speed2 || kvp.Value.BonusSpeed > 100) && !kvp.Value.IsExpired)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var speedSkillId in activeSpeedBuffs)
                await CancelAsync(target, speedSkillId);
        }

        var existingKey = buffType == BuffType.None
            ? 0
            : target.ActiveBuffs
                .FirstOrDefault(kvp => kvp.Value.BuffType == buffType && !kvp.Value.IsExpired)
                .Key;
        if (existingKey > 0)
        {
            if (!isDebuff)
                return (BuffOutcome.Refused, target);
        }

        logger.LogDebug("Buff {SkillId} applied to {Name} type={BuffType} duration={Duration}s",
            skillId, target.Name, buffType, type4Data.Duration);

        target.ReplaceBuff(existingKey, skillId, new ActiveBuff
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
        });

        target.RecalculateStatsWithBuffs(gameDataService);
        await userNotificationService.SendStatUpdateAsync(target);
        await controlStateService.SyncAsync(target);
        await SyncSizeStateAsync(target);
        await SyncDevilFormAsync(target);

        var statusType = GetPartyStatusCode(buffType);
        if (statusType > 0 && isDebuff)
            await combatNotificationService.SendPartyStatusUpdateAsync(target, statusType, applied: true);

        return (BuffOutcome.Applied, target);
    }

    private async Task SyncDevilFormAsync(UserSession session)
    {
        if (DevilForms.Sync(session) is { } devil)
            await sessionManager.Regions.SendToRegion(session, devil, excludeSender: false);
    }

    private async Task SyncSizeStateAsync(UserSession session)
    {
        var state = SizeStates.Current(session);
        if (state == session.SizeState)
            return;
        session.SizeState = state;
        if (session.IsBlinking)
            return;
        await sessionManager.Regions.SendToRegion(
            session,
            MovementPacketWriter.StateChange(session.CharacterId, (byte)StateChangeType.Abnormal, (byte)state),
            excludeSender: false);
    }

    private static bool LandsDebuff(MagicData magic) =>
        magic.SuccessRate is 0 or >= 100 || Random.Shared.Next(100) < magic.SuccessRate;

    private static bool LandsOnPlayer(
        UserSession caster, UserSession target, MagicData magic, BuffType buffType, bool isPrimary)
    {
        if (!StatusEffectChance.LocksMovement(buffType) || magic.PrimaryType == MagicSkillType.Melee)
            return LandsDebuff(magic);

        return IsExtendedRow(magic, isPrimary)
            || StatusEffectChance.Lands(caster, target, buffType, StatusEffectChance.StatusChance);
    }

    private static bool IsExtendedRow(MagicData magic, bool isPrimary) =>
        !isPrimary && magic.PrimaryType == MagicSkillType.Buff;

    private Task AnnounceBuffAsync(
        UserSession caster, int targetId, int skillId, MagicType4Data type4Data, int[] data) =>
        sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                targetId,
                [data[0], SkillSucceeded, data[2], type4Data.Duration, data[4], type4Data.Speed, data[6]]),
            excludeSender: false);

    private IEnumerable<UserSession> AreaPlayers(
        UserSession caster, SkillMoral moral, float radius, float centreX, float centreZ)
    {
        foreach (var player in sessionManager.Regions.GetNearbyUsers(caster).Prepend(caster))
        {
            if (player.Hp <= 0 || player.ZoneId != caster.ZoneId || !AcceptsAreaPlayer(caster, player, moral))
                continue;

            if (WithinRadius(player.X, player.Z, centreX, centreZ, radius))
                yield return player;
        }
    }

    private IEnumerable<NpcInstance> AreaNpcs(UserSession caster, float radius, float centreX, float centreZ)
    {
        foreach (var npc in sessionManager.Regions.GetNearbyNpcs(caster))
        {
            if (IsDebuffableNpc(npc, caster) && WithinRadius(npc.X, npc.Z, centreX, centreZ, radius))
                yield return npc;
        }
    }

    private static bool AcceptsAreaPlayer(UserSession caster, UserSession player, SkillMoral moral)
    {
        var isCaster = player.CharacterId == caster.CharacterId;
        return moral switch
        {
            SkillMoral.Party or SkillMoral.PartyAll => isCaster
                || (caster.IsInParty && player.PartyIndex == caster.PartyIndex),
            SkillMoral.AreaFriend or SkillMoral.SelfArea or SkillMoral.FriendWithMe or SkillMoral.FriendExceptMe
                or SkillMoral.SiegeWeapon => isCaster || !PvpRules.IsEnemy(caster, player),
            SkillMoral.AreaAll => isCaster || PvpRules.CanAttackPlayer(caster, player),
            _ => !isCaster && PvpRules.CanAttackPlayer(caster, player),
        };
    }

    private static bool IsHostileMoral(SkillMoral moral) =>
        moral is SkillMoral.Enemy or SkillMoral.AreaEnemy or SkillMoral.Npc;

    private static bool IsDebuffableNpc(NpcInstance npc, UserSession caster) =>
        npc.IsAlive && npc.ZoneId == caster.ZoneId && NpcHostility.IsAttackableBy(npc, caster);

    private static bool WithinRadius(float x, float z, float centreX, float centreZ, float radius)
    {
        if (radius <= 0)
            return true;

        var dx = x - centreX;
        var dz = z - centreZ;
        return dx * dx + dz * dz <= radius * radius;
    }

    private static float AreaCoordinate(int value, float fallback) => value == 0 ? fallback : value;

    private UserSession? ResolveBuffTarget(UserSession caster, MagicData magic, int targetId)
    {
        if ((SkillMoral)magic.Moral == SkillMoral.Self || targetId == AreaTarget || targetId == caster.CharacterId)
            return caster;

        return sessionManager.GetByCharacterId(targetId);
    }

    private const byte PotionItemGroup = 9;

    private async Task<bool> ExecuteSpecialAsync(UserSession caster, MagicData magic, int skillId, int targetId)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType5Table, magic, skillId, out var type5Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        var special = (SpecialMagicType)type5Data.Type;

        if (targetId == AreaTarget && IsCleanse(special) && IsAreaMoral((SkillMoral)magic.Moral))
        {
            var moral = (SkillMoral)magic.Moral;
            var side = PvpRules.CureMoral(moral, special == SpecialMagicType.RemoveBless);
            foreach (var member in AreaPlayers(caster, side, magic.Range > 0 ? magic.Range + PlayerReachSlack : 0, caster.X, caster.Z).ToList())
            {
                await CleanseAsync(member, special);
                await sessionManager.Regions.SendToRegion(
                    caster,
                    MagicProcessPacketWriter.Create(
                        MagicProcessOpcode.Effecting, skillId, (short)caster.CharacterId,
                        (short)member.CharacterId, [0, SkillSucceeded]),
                    excludeSender: false);
            }
            return true;
        }

        // Sub-types 4 (self-resurrect) and 6 (life crystal) always target the caster.
        UserSession target;
        if (special is SpecialMagicType.ResurrectionSelf or SpecialMagicType.LifeCrystal)
            target = caster;
        else
        {
            var resolved = targetId == AreaTarget && IsCleanse(special) ? caster : sessionManager.GetByCharacterId(targetId);
            if (resolved == null || (IsCleanse(special) && !MayCleanse(caster, resolved, magic, special)))
            {
                await SendMagicFailAsync(caster, skillId);
                return false;
            }
            target = resolved;
        }

        switch (special)
        {
            case SpecialMagicType.RemoveDot:
            case SpecialMagicType.RemoveBuff:
            case SpecialMagicType.RemoveBless:
                await CleanseAsync(target, special);
                break;

            case SpecialMagicType.Resurrection:
            case SpecialMagicType.ResurrectionSelf:
            case SpecialMagicType.LifeCrystal:
            {
                if (target.Hp > 0)
                {
                    await SendMagicFailAsync(caster, skillId);
                    return false;
                }

                if (special == SpecialMagicType.Resurrection)
                {
                    await resurrectionOfferService.OfferAsync(caster, target, skillId);
                    return true;
                }

                if (!await magicItemUsageService.TryConsumeSkillItemAsync(caster, magic))
                {
                    await SendMagicFailAsync(caster, skillId);
                    return false;
                }

                await resurrectionOfferService.ReviveAsync(caster, target, skillId, type5Data);
                return true;
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
        return true;
    }

    private static bool IsCleanse(SpecialMagicType special) =>
        special is SpecialMagicType.RemoveDot or SpecialMagicType.RemoveBuff or SpecialMagicType.RemoveBless;

    private static bool IsAreaMoral(SkillMoral moral) =>
        moral is SkillMoral.PartyAll or SkillMoral.AreaEnemy or SkillMoral.AreaFriend or SkillMoral.AreaAll
            or SkillMoral.SelfArea;

    private static bool MayCleanse(UserSession caster, UserSession target, MagicData magic, SpecialMagicType special)
    {
        var moral = (SkillMoral)magic.Moral;
        if (moral == SkillMoral.Self || target == caster)
            return target == caster && !IsHostileMoral(moral);

        return target.Hp > 0
            && target.ZoneId == caster.ZoneId
            && target.Room == caster.Room
            && AttackReach.Within(caster, target.X, target.Z,
                (magic.Range > 0 ? magic.Range : AttackReach.Melee) + AttackReach.PlayerBodyRadius * 2 + AttackReach.SkillSlack)
            && PvpRules.MayTouch(caster, target, PvpRules.CureMoral(moral, special == SpecialMagicType.RemoveBless), healStrikes: false);
    }

    private async Task CleanseAsync(UserSession target, SpecialMagicType special)
    {
        if (special == SpecialMagicType.RemoveDot)
        {
            var harmful = target.ActiveOverTimeEffects
                .Where(kv => kv.Value.TickAmount < 0)
                .ToList();
            if (harmful.Count == 0) return;

            var clearedCodes = harmful
                .Select(kv => kv.Value.PartyStatusCode)
                .Where(c => c > 0)
                .Distinct()
                .ToList();

            foreach (var (id, _) in harmful)
                target.ActiveOverTimeEffects.TryRemove(id, out _);

            await target.Client.SendPacket(MagicProcessPacketWriter.CreateDurationExpired(DamageOverTimeCured));

            foreach (var code in clearedCodes)
                await combatNotificationService.SendPartyStatusUpdateAsync(target, code, applied: false);
            return;
        }

        if (await ClearCastBuffsAsync(target, helpful: special == SpecialMagicType.RemoveBless))
        {
            target.RebuildSpecialStates(gameDataService);
            target.RecalculateStatsWithBuffs(gameDataService);
            await userNotificationService.SendStatUpdateAsync(target);
        }
    }

    private bool IsHelpfulBuff(int magicId, ActiveBuff buff) =>
        gameDataService.GetMagic(magicId) is { } magic
        && MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, magicId, out var type4Data)
            ? MagicBuffClassifier.IsBuff(type4Data)
            : MagicBuffClassifier.IsBuff(new MagicType4Data { BuffType = (byte)buff.BuffType });

    private async Task<bool> ClearCastBuffsAsync(UserSession target, bool helpful)
    {
        var liftedIds = target.ActiveBuffs
            .Where(kv => kv.Value.CasterId != target.CharacterId && kv.Value.BuffType != BuffType.None
                && IsHelpfulBuff(kv.Key, kv.Value) == helpful)
            .Select(kv => kv.Key)
            .ToList();
        if (liftedIds.Count == 0)
            return false;

        foreach (var id in liftedIds)
        {
            if (target.RemoveBuff(id, out var removed) && removed.BuffType != BuffType.None)
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

    private async Task<bool> ExecuteTransformAsync(UserSession caster, MagicData magic, int skillId, int targetId)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType6Table, magic, skillId, out var type6Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        var target = (SkillMoral)magic.Moral == SkillMoral.Self
            ? caster
            : sessionManager.GetByCharacterId(targetId) ?? caster;

        var use = (TransformationUse)type6Data.UserSkillUse;
        if (SiegeRules.IsSiegeTransformation(use)
            && !SiegeRules.SiegeTransformationAllowed(skillId, caster.ZoneId, sessionManager.Battle.CastleSiegeOpen))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        switch (use)
        {
            case TransformationUse.Monster
                when BattleZoneManager.IsBattleZone(caster.ZoneId) || BattleZoneManager.IsPvpZone(caster.ZoneId):
            case TransformationUse.GuardTower:
                await SendMagicFailAsync(caster, skillId);
                return false;
            case TransformationUse.MovingTower:
                if (caster.ZoneId != ZoneDelos)
                {
                    await SendMagicFailAsync(caster, skillId);
                    return false;
                }
                break;
        }

        if (!HasTransformationItems(caster, magic, type6Data)
            || !IsTransformationClassAllowed(caster.Class, type6Data.Class))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (type6Data.Nation != (byte)EntityNation.All && (byte)target.Nation != type6Data.Nation)
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (target.IsTransformed)
        {
            logger.LogWarning(
                "Transformation {SkillId} refused for {Name}: already transformed as {TransformId}",
                skillId, caster.Name, target.TransformId);
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (type6Data.SkillSuccessRate > 0 && type6Data.SkillSuccessRate < 100
            && Random.Shared.Next(100) >= type6Data.SkillSuccessRate)
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (!await SpendTransformationItemsAsync(caster, magic, type6Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        await ApplyTransformationAsync(caster, target, skillId, type6Data);
        return true;
    }

    public async Task<bool> TransformAsync(UserSession target, int skillId)
    {
        if (target.IsTransformed || gameDataService.GetMagic(skillId) is not { } magic
            || !MagicTypeLookup.TryResolve(gameDataService.MagicType6Table, magic, skillId, out var type6Data))
            return false;

        await ApplyTransformationAsync(target, target, skillId, type6Data);
        return true;
    }

    private async Task ApplyTransformationAsync(UserSession caster, UserSession target, int skillId, MagicType6Data type6Data)
    {
        target.TransformId = type6Data.TransformId;
        target.TransformSkillId = skillId;
        target.SetBuff(skillId, new ActiveBuff
        {
            MagicId = skillId,
            CasterId = caster.CharacterId,
            Duration = type6Data.Duration,
            ExpireTicks = DateTime.UtcNow.AddSeconds(type6Data.Duration).Ticks
        });

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
    }

    private const byte ZoneDelos = (byte)ZoneId.Delos;

    private const byte ZoneForgottenTemple = (byte)ZoneId.ForgottenTemple;
    private const byte ZoneDungeonDefence = (byte)ZoneId.DungeonDefence;

    private async Task<bool> ExecuteStealthAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType9Table, magic, skillId, out var type9Data))
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
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
            MagicStealthType.GuardSummon => await SummonGuardAsync(caster, skillId, type9Data, data),
            MagicStealthType.PetSummon => await CallPetAsync(caster, magic, skillId, data),
            _ => UnhandledStealth(caster, stealthType, skillId),
        };

        if (!applied)
        {
            await SendMagicFailAsync(caster, skillId);
            return false;
        }

        if (stealthType is MagicStealthType.DispelOnMove or MagicStealthType.DispelOnAttack)
            await AnnounceStealthAsync(caster, target, skillId, type9Data, data);
        return true;
    }

    private async Task<bool> HideAsync(
        UserSession caster, UserSession target, MagicStealthType stealthType, int skillId,
        MagicType9Data type9Data)
    {
        if (caster.ZoneId == ZoneForgottenTemple || caster.ZoneId == ZoneDungeonDefence)
            return false;

        if (target.StealthProhibited || target.IsInvisible)
            return false;

        await stealthService.HideAsync(target, StealthRules.InvisibilityOf(gameDataService, skillId, stealthType));
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

    private const int GuardCount = 1;

    private async Task<bool> SummonGuardAsync(UserSession caster, int skillId, MagicType9Data type9Data, int[] data)
    {
        if (type9Data.MonsterNum <= 0)
            return false;

        await DismissGuardAsync(caster);
        var guards = await npcSummonService.SummonAsync(
            type9Data.MonsterNum, caster.ZoneId, caster.Room, (int)caster.X, (int)caster.Z, GuardCount, caster.Y,
            guard =>
            {
                guard.NpcType = NpcData.TypeGuardSummon;
                guard.Nation = (EntityNation)caster.Nation;
                guard.OwnerCharId = caster.CharacterId;
                guard.IsAggressive = false;
            });
        if (guards.Count == 0)
            return false;

        caster.SummonedGuard = guards[0];

        AddStealthBuff(caster, caster, skillId, type9Data);
        await AnnounceStealthAsync(caster, caster, skillId, type9Data, data);
        return true;
    }

    private async Task<bool> CallPetAsync(UserSession caster, MagicData magic, int skillId, int[] data)
    {
        if (skillId == PetService.FamiliarChannelingSkill)
        {
            logger.LogDebug("Familiar channeling from {Name} is not implemented", caster.Name);
            return false;
        }

        if (!magicItemUsageService.CanUseSkillItems(caster, magic)
            || !await petService.SummonAsync(caster) || caster.Pet?.Npc is not { } pet)
            return false;

        await magicItemUsageService.TryConsumeSkillItemAsync(caster, magic);

        await caster.Client.SendPacket(MagicProcessPacketWriter.Create(
            MagicProcessOpcode.Effecting,
            skillId,
            (short)caster.CharacterId,
            (short)caster.CharacterId,
            [data[0], SkillSucceeded, data[2], PetEffectNoDuration, pet.UniqueId, data[5], data[6]]));
        return true;
    }

    private bool SummonsPet(int skillId) =>
        gameDataService.GetMagic(skillId) is { } magic
        && MagicTypeLookup.TryResolve(gameDataService.MagicType9Table, magic, skillId, out var type9)
        && (MagicStealthType)type9.StateChange == MagicStealthType.PetSummon;

    public async Task DismissGuardAsync(UserSession session)
    {
        if (session.SummonedGuard is not { } guard)
            return;

        session.SummonedGuard = null;
        await npcLifecycleService.DespawnAsync(guard);
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

        target.SetBuff(skillId, new ActiveBuff
        {
            MagicId = skillId,
            CasterId = caster.CharacterId,
            Duration = type9Data.Duration,
            ExpireTicks = DateTime.UtcNow.AddSeconds(type9Data.Duration).Ticks
        });
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
