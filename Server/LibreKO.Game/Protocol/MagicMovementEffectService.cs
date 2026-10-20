using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IMagicMovementEffectService
{
    Task<bool> ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data);
}

public class MagicMovementEffectService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IWorldMovementService worldMovementService,
    ISoccerService soccerService,
    ILogger<MagicMovementEffectService> logger) : IMagicMovementEffectService
{
    public async Task<bool> ExecuteAsync(UserSession caster, MagicData magic, int skillId, int targetId, int[] data)
    {
        if (!MagicTypeLookup.TryResolve(gameDataService.MagicType8Table, magic, skillId, out var type8Data))
        {
            await caster.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, caster.CharacterId));
            return false;
        }

        var warpType = (MagicWarpType)type8Data.WarpType;
        bool? moved = warpType switch
        {
            MagicWarpType.BindPoint => await WarpToBindPointAsync(caster, magic),
            MagicWarpType.SummonInZone => await SummonToCasterAsync(caster, magic, targetId),
            MagicWarpType.MoveToTarget => await MoveToTargetAsync(caster, magic, type8Data, targetId),
            MagicWarpType.Blink => await BlinkAsync(caster, type8Data, data),
            MagicWarpType.Rush => await RushAsync(caster, magic, targetId, data),
            MagicWarpType.Pull => await PullAsync(caster, magic, targetId, data),
            MagicWarpType.BallControl => await soccerService.KickAsync(caster, targetId, data),
            _ => Unhandled(warpType, skillId, caster),
        };

        if (moved == false)
        {
            await caster.Client.SendPacket(MagicProcessPacketWriter.CreateFail(skillId, caster.CharacterId));
            return false;
        }

        await sessionManager.Regions.SendToRegion(
            caster,
            MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                skillId,
                (short)caster.CharacterId,
                targetId,
                data),
            excludeSender: false);
        return moved == true;
    }

    private bool? Unhandled(MagicWarpType warpType, int skillId, UserSession caster)
    {
        logger.LogDebug(
            "Warp type {WarpType} has no destination rule: skill={SkillId} caster={Name}",
            warpType, skillId, caster.Name);
        return null;
    }

    private async Task<bool> WarpToBindPointAsync(UserSession caster, MagicData magic)
    {
        if ((SkillMoral)magic.Moral != SkillMoral.PartyAll)
            return await SendHomeAsync(caster);

        var party = caster.IsInParty ? sessionManager.Parties.GetParty(caster.PartyIndex) : null;
        if (party == null)
            return await SendHomeAsync(caster);

        var warped = false;
        foreach (var memberId in party.MemberIds)
        {
            if (memberId < 0)
                continue;

            var member = sessionManager.GetByCharacterId(memberId);
            if (member == null || member.Hp <= 0)
                continue;

            warped |= await SendHomeAsync(member);
        }

        return warped;
    }

    private async Task<bool> SendHomeAsync(UserSession caster)
    {
        var bindEvent = caster.Quest.BindPoint > 0
            ? sessionManager.Maps?.GetObjectEvent(caster.ZoneId, caster.Quest.BindPoint)
            : null;

        if (bindEvent is { Life: ObjectEventAlive })
        {
            await worldMovementService.WarpAsync(caster, ToTenths(bindEvent.PosX), ToTenths(bindEvent.PosZ));
            return true;
        }

        var startPosition = gameDataService.GetStartPosition(caster.ZoneId);
        if (startPosition == null)
            return false;

        var (x, z) = startPosition.RandomSpawn(caster.Nation);
        await worldMovementService.WarpAsync(caster, ToTenths(x), ToTenths(z));
        return true;
    }

    private async Task<bool> SummonToCasterAsync(UserSession caster, MagicData magic, int targetId)
    {
        var target = sessionManager.GetByCharacterId(targetId);
        // A trade, a stall or gathering counts on the player staying where they are.
        if (target == null
            || target.CharacterId == caster.CharacterId
            || target.IsBusy
            || target.ZoneId != caster.ZoneId
            || target.Room != caster.Room
            || !MaySummon(caster, target, (SkillMoral)magic.Moral)
            || target.ActiveBuffs.Values.Any(b => b.BuffType == BuffType.FragmentOfManes && !b.IsExpired))
            return false;

        await worldMovementService.WarpAsync(target, (ushort)caster.GetPosX, (ushort)caster.GetPosZ);
        return true;
    }

    private static bool MaySummon(UserSession caster, UserSession target, SkillMoral moral) => moral switch
    {
        SkillMoral.Party or SkillMoral.PartyAll => caster.IsInParty && target.PartyIndex == caster.PartyIndex,
        SkillMoral.Clan or SkillMoral.ClanAll => caster.KnightsId > 0 && target.KnightsId == caster.KnightsId,
        _ => !PvpRules.IsHostile(caster, target),
    };

    private async Task<bool> MoveToTargetAsync(
        UserSession caster, MagicData magic, MagicType8Data type8Data, int targetId)
    {
        var target = sessionManager.GetByCharacterId(targetId);
        if (target == null
            || target.CharacterId == caster.CharacterId
            || target.Hp <= 0
            || target.ZoneId != caster.ZoneId)
            return false;

        if ((SkillMoral)magic.Moral < SkillMoral.Enemy && PvpRules.IsEnemy(caster, target))
            return false;

        if ((SkillMoral)magic.Moral == SkillMoral.Party
            && (!caster.IsInParty || target.PartyIndex != caster.PartyIndex))
            return false;

        if (type8Data.Radius > 0)
        {
            float dx = target.X - caster.X, dz = target.Z - caster.Z;
            float reach = type8Data.Radius + BlinkReachSlack;
            if (dx * dx + dz * dz > reach * reach)
                return false;
        }

        await worldMovementService.WarpAsync(caster, (ushort)target.GetPosX, (ushort)target.GetPosZ);
        return true;
    }

    private async Task<bool> BlinkAsync(UserSession caster, MagicType8Data type8Data, int[] data)
    {
        if (data.Length <= BlinkZSlot)
            return false;

        var tenthsX = unchecked((ushort)data[BlinkXSlot]);
        var tenthsZ = unchecked((ushort)data[BlinkZSlot]);
        float x = tenthsX / TenthsPerMetre, z = tenthsZ / TenthsPerMetre;
        float dx = x - caster.X, dz = z - caster.Z;
        float reach = type8Data.Radius + BlinkReachSlack;
        if (dx * dx + dz * dz > reach * reach)
            return false;

        if (sessionManager.Maps?.IsValidPosition(caster.ZoneId, x, z) == false)
            return false;

        await worldMovementService.WarpAsync(caster, tenthsX, tenthsZ);
        return true;
    }

    private async Task<bool> RushAsync(UserSession caster, MagicData magic, int targetId, int[] data)
    {
        if (DashTarget(caster, targetId) is not { } target
            || DashPoint(data) is not { } point
            || !DashPointValid(caster, point, origin: caster, landing: target, magic.Range))
            return false;

        data[DashResultSlot] = DashSucceeded;
        await worldMovementService.DisplaceAsync(caster, point.X, point.Z);
        return true;
    }

    private async Task<bool> PullAsync(UserSession caster, MagicData magic, int targetId, int[] data)
    {
        if (DashTarget(caster, targetId) is not { } target
            || DashPoint(data) is not { } point
            || !DashPointValid(caster, point, origin: target, landing: caster, magic.Range))
            return false;

        if (Random.Shared.Next(PercentRoll) >= PullSuccessPercent)
        {
            data[DashResultSlot] = DashFailed;
            return true;
        }

        data[DashResultSlot] = DashSucceeded;
        await worldMovementService.DisplaceAsync(target, point.X, point.Z);
        return true;
    }

    private UserSession? DashTarget(UserSession caster, int targetId) =>
        sessionManager.GetByCharacterId(targetId) is { } target
        && target.CharacterId != caster.CharacterId
        && target.Hp > 0
        && target.ZoneId == caster.ZoneId
        && PvpRules.CanAttackPlayer(caster, target)
            ? target
            : null;

    private static (float X, float Z)? DashPoint(int[] data) =>
        data.Length > BlinkZSlot
            ? (unchecked((ushort)data[BlinkXSlot]) / TenthsPerMetre, unchecked((ushort)data[BlinkZSlot]) / TenthsPerMetre)
            : null;

    private bool DashPointValid(UserSession caster, (float X, float Z) point, UserSession origin, UserSession landing, int range)
    {
        float travel = Distance(origin.X, origin.Z, point.X, point.Z);
        float beside = Distance(landing.X, landing.Z, point.X, point.Z);
        return travel <= range + DashReachSlack
            && beside <= DashLandingSlack
            && sessionManager.Maps?.IsValidPosition(caster.ZoneId, point.X, point.Z) != false;
    }

    private static float Distance(float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private const int DashResultSlot = 1;
    private const int DashSucceeded = 1;
    private const int DashFailed = 0;
    private const int PercentRoll = 100;
    private const int PullSuccessPercent = 50;
    private const float DashReachSlack = 4f;
    private const float DashLandingSlack = 4f;

    private const int BlinkXSlot = 0;
    private const int BlinkZSlot = 2;
    private const float BlinkReachSlack = 2f;
    private const float TenthsPerMetre = 10f;

    private const byte ObjectEventAlive = 1;

    private static ushort ToTenths(float value) => (ushort)(value * 10);

    private static ushort ToTenths(short value) => (ushort)(value * 10);
}
