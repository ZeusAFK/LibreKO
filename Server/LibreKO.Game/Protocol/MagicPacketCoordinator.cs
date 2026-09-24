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
    ILogger<MagicPacketCoordinator> logger) : IMagicPacketCoordinator
{
    private const int OverTimeFinalizeDelayMs = 120;

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
            await SendMagicFailAsync(session, skillId);
            return;
        }

        if (!session.CanUseSkills && !IsTerminator((MagicProcessOpcode)magicOpcode))
        {
            await SendMagicFailAsync(session, skillId);
            return;
        }

        if (!Enum.IsDefined(typeof(MagicProcessOpcode), magicOpcode))
        {
            logger.LogWarning("Unknown magic sub-opcode {SubOpcode} from {Name} skill={SkillId} type={SkillType}",
                magicOpcode, session.Name, skillId, magic.PrimaryType);
            await SendMagicFailAsync(session, skillId);
            return;
        }

        if (RequiresLearnedSkill((MagicProcessOpcode)magicOpcode)
            && !MagicSkillRequirement.IsMet(magic, session.Level, session.SkillPoints))
        {
            logger.LogWarning(
                "Refusing unlearned skill {SkillId} from {Name}: needs {Requirement}, has level {Level} and mastery [{Mastery}]",
                skillId,
                session.Name,
                MagicSkillRequirement.Describe(magic),
                session.Level,
                string.Join(",", session.SkillPoints));
            await SendMagicFailAsync(session, skillId);
            return;
        }

        switch ((MagicProcessOpcode)magicOpcode)
        {
            case MagicProcessOpcode.Casting:
                if (!AllowTiming(session, magic, magicTimingService.CheckCasting(session, magic), magicOpcode))
                {
                    await SendMagicFailAsync(session, skillId);
                    return;
                }

                magicTimingService.OnCastAccepted(session, magic);
                await HandleCastingAsync(session, magic, targetId, data);
                break;
            case MagicProcessOpcode.Flying:
                if (!AllowTiming(session, magic, magicTimingService.CheckRelease(session, magic), magicOpcode))
                {
                    await SendMagicFailAsync(session, skillId);
                    return;
                }

                await HandleFlyingAsync(session, magic, targetId, data);
                break;
            case MagicProcessOpcode.Effecting:
                if (!AllowTiming(session, magic, magicTimingService.CheckRelease(session, magic), magicOpcode))
                {
                    await SendMagicFailAsync(session, skillId);
                    return;
                }

                magicTimingService.OnReleaseAccepted(session, magic);

                if (OpensTransformationList(session, magic))
                {
                    await session.Client.SendPacket(
                        MagicProcessPacketWriter.CreateTransformationList(skillId));
                    return;
                }

                await HandleClientExecutionAsync(session, magic, skillId, targetId, data, magicOpcode);
                break;
            case MagicProcessOpcode.Fail when magic.PrimaryType == MagicSkillType.OverTime
                && session.CastingSkillId != skillId:
                await HandleClientExecutionAsync(session, magic, skillId, targetId, data, magicOpcode);
                break;
            case MagicProcessOpcode.Fail:
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
        }
    }

    private bool OpensTransformationList(UserSession session, MagicData magic) =>
        magic.PrimaryType == MagicSkillType.None
        && magic.SecondaryType != MagicSkillType.None
        && magic.UseItem != 0
        && magicItemUsageService.CanUseSkillItems(session, magic)
        && !BattleZoneManager.IsBattleZone(session.ZoneId)
        && !BattleZoneManager.IsPvpZone(session.ZoneId);

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
            && MagicBuffClassifier.IsBuff(type4Data),
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
        int[] data,
        byte magicOpcode)
    {
        if (magic.PrimaryType == MagicSkillType.Melee)
            return ExecuteMeleeWithManaCostAsync(session, magic, skillId, targetId, data);

        if (magic.PrimaryType != MagicSkillType.OverTime)
            return ExecuteOtherWithManaCostAsync(session, magic, skillId, targetId, data);

        if (magicOpcode == (byte)MagicProcessOpcode.Fail)
        {
            if (session.PendingOverTimeExecution?.SkillId == skillId)
                session.PendingOverTimeExecution = null;

            if (IsClientReportedMiss(session, targetId, data))
                return Task.CompletedTask;

            return ExecuteOverTimeWithManaCostAsync(session, magic, skillId, targetId, data);
        }

        if (!ShouldDelayOverTimeExecution(skillId))
            return ExecuteOverTimeWithManaCostAsync(session, magic, skillId, targetId, data);

        var token = ++session.PendingOverTimeToken;
        var pendingExecution = new PendingMagicExecution
        {
            Token = token,
            SkillId = skillId,
            TargetId = targetId,
            Data = [.. data]
        };

        session.PendingOverTimeExecution = pendingExecution;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(OverTimeFinalizeDelayMs);
                if (session.PendingOverTimeExecution?.Token != token)
                    return;

                session.PendingOverTimeExecution = null;
                await ExecuteOverTimeWithManaCostAsync(
                    session,
                    magic,
                    pendingExecution.SkillId,
                    pendingExecution.TargetId,
                    pendingExecution.Data);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Deferred type-3 execution failed for {Name} skill={SkillId}",
                    session.Name,
                    skillId);
            }
        });

        return Task.CompletedTask;
    }

    private bool ShouldDelayOverTimeExecution(int skillId) =>
        gameDataService.MagicType3Table.TryGetValue(skillId, out var type3Data)
        && type3Data.Radius > 0;

    private static bool IsClientReportedMiss(UserSession session, int targetId, int[] data) =>
        targetId == session.CharacterId
        && data.Length > 3
        && data[3] <= -100
        && !HasAreaCoordinates(data);

    private static bool HasAreaCoordinates(int[] data) =>
        (data.Length > 0 && data[0] != 0)
        || (data.Length > 2 && data[2] != 0);

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
            await SendMagicFailAsync(session, skillId);
            return;
        }

        if (session.IsInCombat && StealthRules.IsInfiltrationPotion(gameDataService, skillId))
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, StealthRules.InCombatRefusal));
            await SendMagicFailAsync(session, skillId);
            return;
        }

        if (magic.Msp > 0)
        {
            if (session.Mp < magic.Msp)
            {
                await SendMagicFailAsync(session, skillId);
                return;
            }

            session.Mp -= (short)magic.Msp;
            await combatLifecycleService.SendMspChangeAsync(session);
        }

        if (!await magicItemUsageService.TryConsumeSkillItemAsync(session, magic))
        {
            await SendMagicFailAsync(session, skillId);
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
        if (magic.PrimaryType != MagicSkillType.Ranged && magic.Msp > 0)
        {
            if (session.Mp < magic.Msp)
            {
                await SendMagicFailAsync(session, skillId);
                return;
            }

            session.Mp -= (short)magic.Msp;
            await combatLifecycleService.SendMspChangeAsync(session);
        }

        await magicExecutionService.ExecuteAsync(session, magic, skillId, targetId, data);
    }

    private async Task ExecuteOverTimeWithManaCostAsync(
        UserSession session,
        MagicData magic,
        int skillId,
        int targetId,
        int[] data)
    {
        if (magic.Msp > 0)
        {
            if (session.Mp < magic.Msp)
            {
                await SendMagicFailAsync(session, skillId);
                return;
            }

            session.Mp -= (short)magic.Msp;
            await combatLifecycleService.SendMspChangeAsync(session);
        }

        await magicExecutionService.ExecuteAsync(session, magic, skillId, targetId, data);
    }

    private async Task HandleFlyingAsync(UserSession session, MagicData magic, int targetId, int[] data)
    {
        if (magic.PrimaryType == MagicSkillType.Ranged)
        {
            if (!MagicTypeLookup.TryResolve(gameDataService.MagicType2Table, magic, magic.Id, out var type2Data))
            {
                await SendMagicFailAsync(session, magic.Id);
                return;
            }

            var requiredArrowCount = Math.Max(1, (int)type2Data.NeedArrow);
            if (magic.UseItem != 0
                && !magicItemUsageService.CanUseItem(session, magic.UseItem, requiredArrowCount))
            {
                await SendMagicFailAsync(session, magic.Id);
                return;
            }

            if (session.Mp < magic.Msp)
            {
                await SendMagicFailAsync(session, magic.Id);
                return;
            }

            if (magic.UseItem != 0
                && !await magicItemUsageService.TryConsumeItemAsync(session, magic.UseItem, requiredArrowCount))
            {
                await SendMagicFailAsync(session, magic.Id);
                return;
            }

            session.Mp -= (short)magic.Msp;
            await combatLifecycleService.SendMspChangeAsync(session);
            magicTimingService.OnVolleyAccepted(session, magic, requiredArrowCount);
        }

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

    private static Task SendMagicFailAsync(UserSession session, int skillId) =>
        session.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, session.CharacterId));
}
