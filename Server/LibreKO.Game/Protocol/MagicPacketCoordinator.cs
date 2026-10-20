using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IMagicPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class MagicPacketCoordinator(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IMagicItemUsageService magicItemUsageService,
    ICombatLifecycleService combatLifecycleService,
    IMagicExecutionService magicExecutionService,
    IMagicTimingService magicTimingService,
    IBuffExtensionService buffExtensionService,
    ILogger<MagicPacketCoordinator> logger) : IMagicPacketCoordinator
{
    private const int ArrowMissedMarker = -101;
    private const int MissMarkerSlot = 3;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var magicOpcode = packet.ReadByte();
        var skillId = packet.ReadInt();
        var casterId = packet.ReadInt();
        var targetId = packet.ReadInt();
        var data = new int[7];
        for (int i = 0; i < 7 && packet.RemainingBytes >= 4; i++)
            data[i] = packet.ReadInt();

        if (casterId != session.CharacterId)
        {
            logger.LogWarning(
                "Refusing magic from {Name}: claimed caster {ClaimedCaster}, session is {CharacterId} skill={SkillId}",
                session.Name, casterId, session.CharacterId, skillId);
            return;
        }

        var magic = gameDataService.GetMagic(skillId);
        if (magic == null)
        {
            logger.LogWarning(
                "Ignoring unknown magic skill {SkillId} from {Name} opcode={MagicOpcode} target={TargetId}",
                skillId,
                session.Name,
                magicOpcode,
                targetId);
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        if ((!session.CanUseSkills || (session.IsBlinking && !RespawnBlinkService.AllowsSkill(magic)))
            && !IsTerminator((MagicProcessOpcode)magicOpcode))
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        // A dead player cannot be hit back, so only the rows that revive the caster work while dead.
        if (session.Hp <= 0
            && RequiresLearnedSkill((MagicProcessOpcode)magicOpcode)
            && !RevivesTheCaster(magic, skillId))
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        if (!ChaosRules.SkillAllowedIn(session.ZoneId, magic.Skill) && !IsTerminator((MagicProcessOpcode)magicOpcode))
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        if (!Enum.IsDefined(typeof(MagicProcessOpcode), magicOpcode))
        {
            logger.LogWarning("Unknown magic sub-opcode {SubOpcode} from {Name} skill={SkillId} type={SkillType}",
                magicOpcode, session.Name, skillId, magic.PrimaryType);
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        if (RequiresLearnedSkill((MagicProcessOpcode)magicOpcode) && MagicSkillRequirement.IsClassPassive(magic))
        {
            logger.LogWarning("Refusing passive skill {SkillId} from {Name}", skillId, session.Name);
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        if (RequiresLearnedSkill((MagicProcessOpcode)magicOpcode) && !session.IsGM
            && !MagicSkillRequirement.IsCastableBy(magic, session.Class))
        {
            logger.LogWarning("Refusing skill {SkillId} from {Name}: not a skill of class {Class}",
                skillId, session.Name, session.Class);
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        if (RequiresLearnedSkill((MagicProcessOpcode)magicOpcode)
            && !MagicSkillRequirement.IsGranted(magic, session.TransformId))
        {
            logger.LogWarning("Refusing skill {SkillId} from {Name}: tree {Tree} needs a nation role or a command form (transform {TransformId})",
                skillId, session.Name, magic.Skill, session.TransformId);
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        if (RequiresLearnedSkill((MagicProcessOpcode)magicOpcode)
            && magic.Skill != ChaosRules.ChaosSkillTree
            && !MagicSkillRequirement.IsMet(magic, session.Level, session.SkillPoints))
        {
            logger.LogWarning(
                "Refusing unlearned skill {SkillId} from {Name}: needs {Requirement}, has level {Level} and mastery [{Mastery}]",
                skillId,
                session.Name,
                MagicSkillRequirement.Describe(magic),
                session.Level,
                string.Join(",", session.SkillPoints));
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        var isFragmentCarrier = session.ActiveBuffs.Values.Any(b => b.BuffType == BuffType.FragmentOfManes && !b.IsExpired);
        if (isFragmentCarrier)
        {
            if (magic.PrimaryType == MagicSkillType.Warp)
            {
                logger.LogWarning("Refusing warp skill {SkillId} from Fragment of Manes carrier {Name}", skillId, session.Name);
                await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
                return;
            }

            if (magic.PrimaryType == MagicSkillType.Buff
                && MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, skillId, out var carrierType4)
                && ((BuffType)carrierType4.BuffType == BuffType.Speed || carrierType4.Speed > 100))
            {
                logger.LogWarning("Refusing speed buff {SkillId} from Fragment of Manes carrier {Name}", skillId, session.Name);
                await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
                return;
            }
        }

        if (RequiresLearnedSkill((MagicProcessOpcode)magicOpcode)
            && (MagicProcessOpcode)magicOpcode != MagicProcessOpcode.Flying
            && (HealOverTimeAlreadyRunning(session, magic, targetId) || MissesRequiredBuff(session, magic)))
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
            return;
        }

        switch ((MagicProcessOpcode)magicOpcode)
        {
            case MagicProcessOpcode.Casting:
                if (!AllowTiming(session, magic, magicTimingService.CheckCasting(session, magic), magicOpcode))
                {
                    await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
                    return;
                }

                magicTimingService.OnCastAccepted(session, magic, armCooldown: InstantMagicBuffOf(session, magic) is null);
                session.IsSitting = false;
                await HandleCastingAsync(session, magic, targetId, data);
                break;
            case MagicProcessOpcode.Flying:
                if (!AllowTiming(session, magic, magicTimingService.CheckRelease(session, magic), magicOpcode)
                    || (magic.PrimaryType == MagicSkillType.Ranged
                        && (!AllowTiming(session, magic, magicTimingService.CheckVolley(session), magicOpcode)
                            || !magicExecutionService.AllowsTarget(session, magic, skillId, targetId, data))))
                {
                    await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
                    return;
                }

                await HandleFlyingAsync(session, magic, targetId, data);
                break;
            case MagicProcessOpcode.Effecting:
                if (!AllowTiming(session, magic, magicTimingService.CheckRelease(session, magic), magicOpcode)
                    || (magic.PrimaryType == MagicSkillType.Ranged && !session.PendingArrowHits.ContainsKey(magic.Id)))
                {
                    await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
                    return;
                }

                if (IsDeathTaunt(session, magic, targetId))
                {
                    magicTimingService.OnReleaseAccepted(session, magic);
                    data[DeathTaunt.MarkerSlot] = DeathTaunt.Marker;
                    await sessionManager.Regions.SendToRegion(
                        session,
                        MagicProcessPacketWriter.Create(MagicProcessOpcode.Effecting, magic.Id, (short)session.CharacterId, targetId, data),
                        excludeSender: false);
                    return;
                }

                if (!OpensTransformationList(session, magic)
                    && !magicExecutionService.AllowsTarget(session, magic, skillId, targetId, data))
                {
                    magicTimingService.OnReleaseRefused(session, magic);
                    await SendMagicFailAsync(session, skillId, MagicFailCode.ForStage((MagicProcessOpcode)magicOpcode));
                    return;
                }

                var instantMagic = InstantMagicBuffOf(session, magic);
                magicTimingService.OnReleaseAccepted(session, magic, armCooldown: instantMagic is null);
                session.IsSitting = false;

                if (OpensTransformationList(session, magic))
                {
                    await session.Client.SendPacket(
                        MagicProcessPacketWriter.CreateTransformationList(skillId));
                    return;
                }

                await HandleClientExecutionAsync(session, magic, skillId, targetId, data);
                if (instantMagic is { } spent)
                    await magicExecutionService.CancelAsync(session, spent);
                break;
            case MagicProcessOpcode.Fail:
                if (magic.PrimaryType == MagicSkillType.Ranged && data[MissMarkerSlot] == ArrowMissedMarker)
                    magicTimingService.OnReleaseAccepted(session, magic);
                else if (session.CastingSkillId == skillId)
                    magicTimingService.OnCastAborted(session, skillId);
                await sessionManager.Regions.SendToRegion(
                    session,
                    MagicProcessPacketWriter.Create(MagicProcessOpcode.Fail, magic.Id, (short)session.CharacterId, targetId, data),
                    excludeSender: true);
                break;
            case MagicProcessOpcode.Cancel:
            case MagicProcessOpcode.SkillValueUpdate:
            case MagicProcessOpcode.DurationExpired:
                magicTimingService.OnCastAborted(session, skillId);
                if (!IsPlayerCancellable(magic, skillId))
                    return;

                await magicExecutionService.CancelAsync(session, skillId);
                break;
            case MagicProcessOpcode.CancelTransformation:
                break;
            case MagicProcessOpcode.ExtendDuration:
                await buffExtensionService.ExtendAsync(session, skillId);
                break;
        }
    }

    private bool IsDeathTaunt(UserSession session, MagicData magic, int targetId)
    {
        MagicTypeLookup.TryResolve(gameDataService.MagicType3Table, magic, magic.Id, out var type3Data);
        if (!DeathTaunt.IsTaunt(magic, type3Data))
            return false;

        if (sessionManager.GetByCharacterId(targetId) is { } player)
            return player.Hp <= 0 && player.ZoneId == session.ZoneId;

        return sessionManager.Regions.GetNpc(targetId) is { IsAlive: false } npc && npc.ZoneId == session.ZoneId;
    }

    private bool HealOverTimeAlreadyRunning(UserSession session, MagicData magic, int targetId)
    {
        if (magic.PrimaryType != MagicSkillType.OverTime
            || !MagicTypeLookup.TryResolve(gameDataService.MagicType3Table, magic, magic.Id, out var type3Data)
            || !MagicOverTimeService.IsHealOverTime(type3Data))
            return false;

        var holder = (SkillMoral)magic.Moral switch
        {
            SkillMoral.Self => session,
            <= SkillMoral.Party => targetId == session.CharacterId || targetId == AreaTarget
                ? session
                : sessionManager.GetByCharacterId(targetId),
            SkillMoral.PartyAll => session,
            _ => null,
        };
        return holder != null && MagicOverTimeService.HasHealOverTime(holder);
    }

    private bool MissesRequiredBuff(UserSession session, MagicData magic)
    {
        if (magic.PrimaryType != MagicSkillType.Melee
            || !MagicTypeLookup.TryResolve(gameDataService.MagicType1Table, magic, magic.Id, out var type1Data)
            || type1Data.RequiredBuffType == 0)
            return false;

        return !session.ActiveBuffs.Values.Any(buff =>
            (byte)buff.BuffType == type1Data.RequiredBuffType && !buff.IsExpired);
    }

    private const int AreaTarget = -1;

    private int? InstantMagicBuffOf(UserSession session, MagicData magic)
    {
        if (IsInstantMagic(magic))
            return null;

        foreach (var (buffSkillId, buff) in session.ActiveBuffs)
            if (buff.BuffType == BuffType.InstantMagic && !buff.IsExpired)
                return buffSkillId;
        return null;
    }

    private bool IsInstantMagic(MagicData magic) =>
        magic.PrimaryType == MagicSkillType.Buff
        && MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, magic.Id, out var type4Data)
        && (BuffType)type4Data.BuffType == BuffType.InstantMagic;

    private bool OpensTransformationList(UserSession session, MagicData magic) =>
        magic.PrimaryType == MagicSkillType.None
        && magic.SecondaryType != MagicSkillType.None
        && magic.UseItem != 0
        && magicItemUsageService.CanUseSkillItems(session, magic)
        && !BattleZoneManager.IsBattleZone(session.ZoneId)
        && !BattleZoneManager.IsPvpZone(session.ZoneId);

    private bool RevivesTheCaster(MagicData magic, int skillId) =>
        magic.PrimaryType == MagicSkillType.Special
        && MagicTypeLookup.TryResolve(gameDataService.MagicType5Table, magic, skillId, out var type5Data)
        && (SpecialMagicType)type5Data.Type is SpecialMagicType.ResurrectionSelf or SpecialMagicType.LifeCrystal;

    private static bool IsTerminator(MagicProcessOpcode opcode) =>
        opcode is MagicProcessOpcode.Cancel
            or MagicProcessOpcode.SkillValueUpdate
            or MagicProcessOpcode.DurationExpired
            or MagicProcessOpcode.CancelTransformation;

    private static bool RequiresLearnedSkill(MagicProcessOpcode opcode) =>
        opcode is MagicProcessOpcode.Casting or MagicProcessOpcode.Flying or MagicProcessOpcode.Effecting;

    private bool IsPlayerCancellable(MagicData magic, int skillId) => magic.PrimaryType switch
    {
        MagicSkillType.Buff =>
            MagicTypeLookup.TryResolve(gameDataService.MagicType4Table, magic, skillId, out var type4Data)
            && MagicBuffClassifier.IsBuff(type4Data)
            && (BuffType)type4Data.BuffType != BuffType.FragmentOfManes,
        MagicSkillType.Transform or MagicSkillType.Stealth => true,
        _ => false,
    };

    private bool AllowTiming(
        UserSession session,
        MagicData magic,
        MagicTimingVerdict verdict,
        byte magicOpcode)
    {
        if (verdict == MagicTimingVerdict.Allowed)
            return true;

        logger.LogDebug(
            "Dropping magic {SubOpcode} from {Name} skill={SkillId}: {Verdict} (cast={CastTime} recast={ReCastTime})",
            (MagicProcessOpcode)magicOpcode,
            session.Name,
            magic.Id,
            verdict,
            magic.CastTime,
            magic.ReCastTime);
        return false;
    }

    private Task HandleClientExecutionAsync(
        UserSession session,
        MagicData magic,
        int skillId,
        int targetId,
        int[] data)
    {
        if (magic.PrimaryType == MagicSkillType.Melee)
            return ExecuteMeleeWithManaCostAsync(session, magic, skillId, targetId, data);

        if (magic.PrimaryType != MagicSkillType.OverTime)
            return ExecuteOtherWithManaCostAsync(session, magic, skillId, targetId, data);

        return ExecuteOverTimeWithManaCostAsync(session, magic, skillId, targetId, data);
    }

    private async Task HandleCastingAsync(UserSession session, MagicData magic, int targetId, int[] data)
    {
        // CASTING phase only broadcasts the cast animation — no MP check, no
        // MP deduction. Mana is consumed at FLYING (Type 2) or EFFECTING
        await sessionManager.Regions.SendToRegion(
            session,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Casting,
                magic.Id,
                (short)session.CharacterId,
                targetId,
                data),
            excludeSender: false);
    }

    private async Task ExecuteMeleeWithManaCostAsync(
        UserSession session,
        MagicData magic,
        int skillId,
        int targetId,
        int[] data)
    {
        if (!magicItemUsageService.CanUseSkillItems(session, magic))
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.NoEffect);
            return;
        }

        if (session.IsInCombat && StealthRules.IsInfiltrationPotion(gameDataService, skillId))
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, StealthRules.InCombatRefusal));
            await SendMagicFailAsync(session, skillId, MagicFailCode.NoEffect);
            return;
        }

        if (!await TryPaySkillCostAsync(session, magic, skillId))
            return;

        if (!await magicItemUsageService.TryConsumeSkillItemAsync(session, magic))
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.NoEffect);
            return;
        }

        await magicExecutionService.ExecuteAsync(session, magic, skillId, targetId, data);
    }

    private async Task ExecuteOtherWithManaCostAsync(
        UserSession session,
        MagicData magic,
        int skillId,
        int targetId,
        int[] data)
    {
        if (magic.PrimaryType != MagicSkillType.Ranged && !await TryPaySkillCostAsync(session, magic, skillId))
            return;

        await magicExecutionService.ExecuteAsync(session, magic, skillId, targetId, data);
    }

    private async Task ExecuteOverTimeWithManaCostAsync(
        UserSession session,
        MagicData magic,
        int skillId,
        int targetId,
        int[] data)
    {
        if (!await TryPaySkillCostAsync(session, magic, skillId))
            return;

        await magicExecutionService.ExecuteAsync(session, magic, skillId, targetId, data);
    }

    private async Task HandleFlyingAsync(UserSession session, MagicData magic, int targetId, int[] data)
    {
        if (magic.PrimaryType == MagicSkillType.Ranged)
        {
            if (!MagicTypeLookup.TryResolve(gameDataService.MagicType2Table, magic, magic.Id, out var type2Data))
            {
                await SendMagicFailAsync(session, magic.Id, MagicFailCode.KillFlying);
                return;
            }

            if (NeedsABow(magic) && AttackReach.HeldBow(session, gameDataService) == null)
            {
                await SendMagicFailAsync(session, magic.Id, MagicFailCode.KillFlying);
                return;
            }

            var requiredArrowCount = Math.Max(1, (int)type2Data.NeedArrow);
            if (magic.UseItem != 0
                && !magicItemUsageService.CanUseItem(session, magic.UseItem, requiredArrowCount))
            {
                await SendMagicFailAsync(session, magic.Id, MagicFailCode.KillFlying);
                return;
            }

            if (session.Mp < magic.Msp)
            {
                await SendMagicFailAsync(session, magic.Id, MagicFailCode.NotEnoughMp);
                return;
            }

            if (magic.UseItem != 0
                && !await magicItemUsageService.TryConsumeItemAsync(session, magic.UseItem, requiredArrowCount))
            {
                await SendMagicFailAsync(session, magic.Id, MagicFailCode.KillFlying);
                return;
            }

            session.Mp -= (short)magic.Msp;
            await combatLifecycleService.SendMspChangeAsync(session);
            magicTimingService.OnVolleyAccepted(session, magic, requiredArrowCount);
        }

        magicTimingService.OnFlightLaunched(session, magic);
        await sessionManager.Regions.SendToRegion(
            session,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Flying,
                magic.Id,
                (short)session.CharacterId,
                targetId,
                data),
            excludeSender: false);
    }

    private bool NeedsABow(MagicData magic) => gameDataService.GetItem(magic.UseItem)?.Category != ItemKind.Etc;

    private async Task<bool> TryPaySkillCostAsync(UserSession session, MagicData magic, int skillId)
    {
        if (session.Mp < magic.Msp)
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.NotEnoughMp);
            return false;
        }

        if (!KurianStamina.CanPay(session, magic.Sp))
        {
            await SendMagicFailAsync(session, skillId, MagicFailCode.NoEffect);
            return false;
        }

        if (magic.Msp > 0)
        {
            session.Mp -= (short)magic.Msp;
            await combatLifecycleService.SendMspChangeAsync(session);
        }

        if (KurianStamina.Spend(session, magic.Sp))
            await session.Client.SendPacket(VitalsPacketWriter.StaminaChange(session.KurianMaxSp, session.KurianSp));

        return true;
    }

    private static Task SendMagicFailAsync(UserSession session, int skillId, int code) =>
        session.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, session.CharacterId, code));
}
